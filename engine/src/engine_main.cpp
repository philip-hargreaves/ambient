#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <exception>
#include <filesystem>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>
#include <vector>

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <shlobj.h>
#include <windows.h>

#ifdef _DEBUG
#include <crtdbg.h>
#endif

#include "adapters/audio/capture_devices.hpp"
#include "adapters/audio/wasapi_capture.hpp"
#include "adapters/audio/wav_source.hpp"
#include "adapters/diarisation/anchor_store.hpp"
#include "adapters/diarisation/deferred_diariser.hpp"
#include "adapters/diarisation/scripted_diariser.hpp"
#include "adapters/diarisation/speaker_diariser.hpp"
#include "adapters/guidance/document_ingest.hpp"
#include "adapters/guidance/embedder.hpp"
#include "adapters/guidance/guidance_lane.hpp"
#include "adapters/guidance/retriever.hpp"
#include "adapters/ipc/handlers.hpp"
#include "adapters/ipc/pipe_server.hpp"
#include "adapters/ipc/wire_events.hpp"
#include "adapters/models/model_store.hpp"
#include "adapters/models/ov_runtime.hpp"
#include "adapters/note/worker_note_writer.hpp"
#include "adapters/storage/sqlite_session_store.hpp"
#include "adapters/system/exe_paths.hpp"
#include "adapters/system/power_throttling.hpp"
#include "adapters/system/process_scan.hpp"
#include "adapters/transcription/scripted_transcriber.hpp"
#include "adapters/transcription/whisper_transcriber.hpp"
#include "adapters/translate/nllb_translator.hpp"
#include "adapters/translate/translate_lane.hpp"
#include "adapters/vad/deferred_vad.hpp"
#include "adapters/vad/passthrough_vad.hpp"
#include "adapters/vad/silero_vad.hpp"
#include "core/common/cli_args.hpp"
#include "core/metrics/metrics.hpp"
#include "core/session/playback.hpp"
#include "core/session/session_controller.hpp"

namespace {

std::filesystem::path StoreRoot(const std::vector<std::string>& args) {
    if (args.size() > 1) return args[1];
    char* local_app_data = nullptr;
    if (_dupenv_s(&local_app_data, nullptr, "LOCALAPPDATA") != 0 || local_app_data == nullptr) {
        throw std::runtime_error("LOCALAPPDATA is not set and no store root was given");
    }
    const auto root = std::filesystem::path(local_app_data) / "ambient" / "store";
    std::free(local_app_data);
    return root;
}

// Added documents live in the user's Documents folder unless a run says otherwise
std::filesystem::path GuidelinesFolder(const std::string& override) {
    if (!override.empty()) return override;
    PWSTR documents = nullptr;
    std::filesystem::path folder;
    if (SHGetKnownFolderPath(FOLDERID_Documents, KF_FLAG_DEFAULT, nullptr, &documents) == S_OK) {
        folder = std::filesystem::path(documents) / "Ambient guidelines";
    }
    CoTaskMemFree(documents);
    if (folder.empty()) throw std::runtime_error("no Documents folder and no --guidelines given");
    return folder;
}

// True when a staged model has never been compiled on this machine
bool Uncompiled(const ambient::models::ModelStore& store, const std::string& role) {
    return !std::filesystem::exists(store.Resolve(role, "default").dir / ".cache");
}

// A replay request plays a wav through the same port. A launch-time wav path
// (CI, scripts) forces every session to replay that file
ambient::session::SourceFactory MakeSourceFactory(std::string forced) {
    return [forced = std::move(forced)](
               const std::optional<ambient::session::ReplaySpec>& replay,
               const std::string& mic_id) -> std::unique_ptr<ambient::audio::IAudioSource> {
        if (replay.has_value()) {
            return std::make_unique<ambient::audio::WavSource>(
                replay->path, ambient::audio::WavSource::Config{replay->speed, replay->monitor,
                                                                replay->start_frame});
        }
        if (!forced.empty()) return std::make_unique<ambient::audio::WavSource>(forced);
        return std::make_unique<ambient::audio::WasapiCapture>(ambient::audio::WideId(mic_id));
    };
}

// Real transcription when the ASR role is staged, scripted otherwise (CI)
std::unique_ptr<ambient::asr::ITranscriber> BuildTranscriber(
    const ambient::models::ModelStore& store, ambient::models::OvRuntime& runtime,
    const std::string& device, ambient::metrics::Registry& metrics, bool& first_use) {
    try {
        first_use |= Uncompiled(store, "asr");
        return std::make_unique<ambient::asr::WhisperTranscriber>(store, runtime, device, &metrics);
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: scripted transcripts (%s)\n", e.what());
        return std::make_unique<ambient::asr::ScriptedTranscriber>();
    }
}

// Compiles behind the serve loop. session/start waits on it, hello does not
std::unique_ptr<ambient::audio::IStreamingVad> BuildVad(const ambient::models::ModelStore& store,
                                                        ambient::models::OvRuntime& runtime,
                                                        ambient::metrics::Registry& metrics) {
    try {
        store.Resolve("vad", "default");
        return std::make_unique<ambient::audio::DeferredVad>(
            [&store, &runtime] {
                return std::make_unique<ambient::audio::SileroVad>(store, runtime);
            },
            &metrics);
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: capped windows (%s)\n", e.what());
        return std::make_unique<ambient::audio::PassthroughVad>();
    }
}

// Diarisation needs both its models, scripted otherwise (CI)
std::unique_ptr<ambient::diar::IDiariser> BuildDiariser(const ambient::models::ModelStore& store,
                                                        ambient::models::OvRuntime& runtime,
                                                        ambient::diar::AnchorStore& anchors,
                                                        ambient::metrics::Registry& metrics) {
    try {
        store.Resolve("diarisation", "default");
        store.Resolve("segmentation", "default");
        return std::make_unique<ambient::diar::DeferredDiariser>(
            [&store, &runtime, &anchors] {
                return std::make_unique<ambient::diar::SpeakerDiariser>(store, runtime, anchors);
            },
            &metrics);
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: scripted speakers (%s)\n", e.what());
        return std::make_unique<ambient::diar::ScriptedDiariser>();
    }
}

// Generation runs in its own supervised process: a GPU driver fault there
// costs a respawn and leaves the engine standing. Null when nothing can write
std::unique_ptr<ambient::note::WorkerNoteWriter> BuildNoteWriter(
    ambient::models::ModelStore& store, const std::filesystem::path& models_root,
    ambient::ipc::PipeServer& server, bool& first_use) {
    try {
        store.Resolve("note", "default");
        const auto host = ambient::system::ExeDir() / "ambient_note_host.exe";
        if (!std::filesystem::exists(host)) {
            // Never write in-process: that is the configuration the driver fault corrupts
            std::fprintf(stderr, "ambient-engine: note DISABLED, %s is missing\n",
                         host.string().c_str());
            return nullptr;
        }
        auto worker = std::make_unique<ambient::note::WorkerNoteWriter>(
            host, models_root, models_root.parent_path() / "prompts", &store);
        // The shell configures the tier on connect. A non-default tier's
        // first compile runs then
        worker->SetListener([&server](const ambient::note::NoteModelState& state) {
            server.PushNotification("note/model", ambient::ipc::NoteModelJson(state));
        });
        // First use only: the one-off compile runs on an idle GPU, ahead of any recording
        if (Uncompiled(store, "note")) {
            first_use = true;
            std::fprintf(stderr, "ambient-engine: first use, compiling the note model\n");
            worker->Prepare();
        }
        return worker;
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: stub note (%s)\n", e.what());
        return nullptr;
    }
}

// Translation runs on the CPU, so it never contends with the GPU. Null when
// the model is not staged
std::unique_ptr<ambient::translate::NllbTranslator> BuildTranslator(
    const ambient::models::ModelStore& store, ambient::models::OvRuntime& runtime,
    bool& first_use) {
    try {
        store.Resolve("translation", "default");
        auto translator = std::make_unique<ambient::translate::NllbTranslator>(store, runtime);
        // The CPU compile joins the one-off warm-up, so the first translation
        // is as fast as every other
        if (Uncompiled(store, "translation")) {
            first_use = true;
            std::fprintf(stderr, "ambient-engine: first use, compiling the translator\n");
            translator->Prepare();
        }
        return translator;
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: no translation (%s)\n", e.what());
        return nullptr;
    }
}

}  // namespace

int main(int argc, char* argv[]) {
#ifdef _DEBUG
    // Assertions and CRT errors go to stderr as text rather than parking a
    // headless engine behind a modal dialog
    _CrtSetReportMode(_CRT_ASSERT, _CRTDBG_MODE_FILE);
    _CrtSetReportFile(_CRT_ASSERT, _CRTDBG_FILE_STDERR);
    _CrtSetReportMode(_CRT_ERROR, _CRTDBG_MODE_FILE);
    _CrtSetReportFile(_CRT_ERROR, _CRTDBG_FILE_STDERR);
#endif
    try {
        // Flags first, then positional: pipe name, store root, models root, replay wav
        std::vector<std::string> args(argv + 1, argv + argc);
        const std::string asr_device = ambient::TakeFlag(args, "--asr-device");
        const std::string corpora_override = ambient::TakeFlag(args, "--corpora");
        const std::string guidelines_override = ambient::TakeFlag(args, "--guidelines");
        // Dev builds only: a demo corpus marked research is searched when set
        const bool include_research = ambient::TakeSwitch(args, "--include-research");
        // Evaluation only: a held-out run must not teach the voiceprint
        const bool freeze_anchor = ambient::TakeSwitch(args, "--freeze-anchor");
        // Whisper and the note host take turns on the GPU. With whisper on the
        // NPU there is nothing to share
        if (asr_device != "NPU") {
            const std::string lease = "Local\\ambient-gpu-" + std::to_string(GetCurrentProcessId());
            _putenv_s("AMBIENT_GPU_LEASE", lease.c_str());
            std::fprintf(stderr, "ambient-engine: note prefill on, GPU lease %s\n", lease.c_str());
        }
        std::fprintf(stderr, "ambient-engine: power throttling %s\n",
                     ambient::system::Describe(ambient::system::DisableThrottlingOnSelf()).c_str());

        std::wstring pipe_name = L"\\\\.\\pipe\\LOCAL\\ambient-engine";
        if (args.size() > 0) {
            pipe_name = L"\\\\.\\pipe\\" + std::wstring(args[0].begin(), args[0].end());
        }
        const std::filesystem::path store_root = StoreRoot(args);
        const std::filesystem::path models_root =
            args.size() > 2 ? std::filesystem::path(args[2]) : ambient::system::DefaultModelsRoot();
        // Guidance corpora sit beside the models, each replaced as a directory
        const std::filesystem::path corpora_root = corpora_override.empty()
                                                       ? models_root.parent_path() / "corpora"
                                                       : std::filesystem::path(corpora_override);

        ambient::ipc::PipeServer server(pipe_name);
        ambient::store::SqliteSessionStore session_store(store_root);
        ambient::ipc::WireEvents events(server, session_store);
        // A consultation left by closing the app is left all the same
        session_store.EraseUnretained();
        ambient::models::ModelStore model_store(models_root);
        ambient::models::OvRuntime ov_runtime;
        ambient::metrics::Registry metrics;
        ambient::diar::AnchorStore anchors(store_root);

        bool first_use = false;
        auto transcriber =
            BuildTranscriber(model_store, ov_runtime, asr_device, metrics, first_use);
        auto vad = BuildVad(model_store, ov_runtime, metrics);
        auto diariser = BuildDiariser(model_store, ov_runtime, anchors, metrics);
        // A host from an engine that has died takes a moment to leave. One
        // still here after that is wedged in the driver, and only a reboot ends it
        const bool stray_note_host =
            !ambient::system::WaitUntilGone(L"ambient_note_host.exe", std::chrono::seconds(5));
        if (stray_note_host) {
            std::fprintf(stderr,
                         "ambient-engine: a note host from an earlier engine is still running; "
                         "the GPU is not ours until the computer restarts\n");
        }
        auto note_writer = BuildNoteWriter(model_store, models_root, server, first_use);
        auto translator = BuildTranslator(model_store, ov_runtime, first_use);
        std::unique_ptr<ambient::translate::TranslateLane> translate_lane;
        if (translator != nullptr) {
            translate_lane = std::make_unique<ambient::translate::TranslateLane>(
                *translator, [&events](const std::string& method, const nlohmann::json& params) {
                    events.OnTranslation(method, params);
                });
            events.SetTranslator(translator.get());
        }
        // Guidance retrieval runs on the CPU in its own lane. The embedder loads
        // in the background so the first note's search is warm
        ambient::guidance::Retriever guidance_retriever(
            [&model_store]() -> std::unique_ptr<ambient::guidance::IEmbedder> {
                return ambient::guidance::Embedder::Load(model_store);
            },
            corpora_root,
            ambient::guidance::RetrieverOptions{.include_research = include_research});
        ambient::guidance::GuidanceLane guidance_lane(
            guidance_retriever, [&server](const ambient::guidance::Readiness& readiness) {
                server.PushNotification("guidance/model",
                                        ambient::ipc::GuidanceModelJson(readiness));
            });
        events.SetGuidance(&guidance_lane);
        guidance_lane.Prepare();

        // 10 s rather than 3: a Bluetooth microphone link waking measured 1.6-8.8 s
        // before first audio. Wired mics answer in well under a second either way
        ambient::session::SessionController controller(
            MakeSourceFactory(args.size() > 3 ? args[3] : std::string()), events, session_store,
            *transcriber, *vad, *diariser, std::chrono::seconds(10),
            5 * ambient::audio::kSampleRate, note_writer.get(), &metrics);
        if (freeze_anchor) controller.FreezeAnchor();

        // Added documents embed between note searches and wait while a consultation runs
        const auto ingest_host = ambient::system::ExeDir() / "ambient_ingest_host.exe";
        ambient::guidance::DocumentIngest ingest(
            guidance_retriever, GuidelinesFolder(guidelines_override), store_root / "documents",
            [&controller] { return controller.Running(); },
            std::filesystem::exists(ingest_host) ? ingest_host : std::filesystem::path());
        ingest.SetListener(
            [&server](const ambient::guidance::IngestProgress& progress) {
                server.PushNotification("guidance/progress", ambient::ipc::ProgressJson(progress));
            },
            [&server](const ambient::guidance::DocumentInfo& document) {
                server.PushNotification("guidance/document", ambient::ipc::DocumentJson(document));
                if (ambient::ipc::ChangesReadySet(document)) {
                    server.PushNotification("guidance/documentsChanged", nlohmann::json::object());
                }
            });
        // Demo playback ends in a review of the copy, its note searched like any other
        ambient::session::Playback playback(
            events, session_store,
            {.finalised = [&controller](const std::string& id) { controller.Open(id); },
             .guidance =
                 [&server, &session_store, &guidance_lane](const std::string& id) {
                     auto note =
                         session_store.ReadDocument(id, ambient::store::DocumentKind::kNote);
                     if (note.text.empty()) return;
                     guidance_lane.Run(ambient::ipc::GuidanceSearchRequest(
                         session_store, id, std::move(note), ambient::ipc::kGuidanceLimit,
                         [&server](const std::string& method, nlohmann::json body) {
                             server.PushNotification(method, std::move(body));
                         }));
                 }});
        ambient::ipc::RegisterMethods(
            server, {.controller = controller,
                     .models = model_store,
                     .sessions = session_store,
                     .metrics = &metrics,
                     .runtime = &ov_runtime,
                     .translator = translator.get(),
                     .translate_lane = translate_lane.get(),
                     .first_use = first_use,
                     .anchors = &anchors,
                     .note_lane = note_writer.get(),
                     .stray_note_host = stray_note_host,
                     .demo_dir = models_root.parent_path() / "demo" / "reflections",
                     .playback = &playback});
        ambient::ipc::RegisterGuidanceMethods(server, session_store, guidance_retriever,
                                              guidance_lane, ingest);
        server.ServeOneClient();
        controller.Stop();
        return 0;
    } catch (const std::exception& e) {
        std::fprintf(stderr, "ambient-engine: %s\n", e.what());
        return 1;
    }
}
