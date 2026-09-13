#pragma once

#include <stdexcept>
#include <string>
#include <vector>

namespace ambient::guidance {

// A guidance corpus the retriever found. The hash, embedder and build date
// name the exact text a result came from. A corpus that failed a load guard is
// listed with the reason and searched by nothing
struct Corpus {
    std::string id;
    std::string name;
    std::string licence;
    std::string attribution;
    std::string source;  // "nice", "text", "upload"
    std::string embedder;
    std::string sha256;
    int chunks = 0;
    std::string built_at;     // ISO 8601
    std::string unavailable;  // empty when loaded; otherwise why not
};

// One recommendation the panel shows
struct Result {
    std::string corpus;
    std::string chunk_id;  // guideline code and recommendation number, "ng100-1_1_1"
    std::string code;      // the code alone, "ng100"
    std::string number;    // recommendation number, "1.1.1"
    std::string title;
    std::string section;
    std::string text;
    std::string url;
    std::string last_updated;  // ISO 8601, from the guideline
    std::string update_tag;    // NICE change marker on the recommendation
    std::string source;        // the corpus source, "nice"/"text"/"upload"
    std::string citation;      // code, number and title on one line, for the clipboard
    double score = 0;
    std::string trigger;  // the note sentence that found it, empty when the note as a whole did
};

struct Results {
    std::vector<Result> shown;
    std::vector<Corpus> searched;  // corpora open to the search, for the record
    int considered = 0;            // candidates before the floor and the filters
    double floor = 0;              // cosine floor the candidates were held to
    bool abstained = false;        // nothing to show, by filter or by floor
};

// Whether the embedder and corpora are usable yet
struct Readiness {
    enum class Phase { kLoading, kReady, kUnavailable };
    Phase phase = Phase::kLoading;
    std::string detail;  // the loader's reason when unavailable
};

// Retrieval for the Guidelines feature: the note in, the recommendations to
// show out, already filtered, ordered and thresholded. Retrieved text never
// enters a generated document. Uploads arrive later; until then the two
// document calls throw
class IGuidanceRetriever {
   public:
    virtual ~IGuidanceRetriever() = default;

    // Starts any slow loading in the background so the first Search is warm;
    // safe to call repeatedly
    virtual void Prepare() {}

    virtual Results Search(const std::string& note, int limit) = 0;

    virtual std::vector<Corpus> Corpora() = 0;

    virtual Readiness Status() = 0;

    virtual void AddDocument(const std::string& /*path*/) {
        throw std::logic_error("guidance documents are not supported yet");
    }

    virtual void RemoveDocument(const std::string& /*corpus_id*/) {
        throw std::logic_error("guidance documents are not supported yet");
    }
};

}  // namespace ambient::guidance
