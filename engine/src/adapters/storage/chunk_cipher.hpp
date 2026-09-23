#pragma once

#include <cstdint>
#include <filesystem>
#include <memory>
#include <span>
#include <string_view>
#include <vector>

namespace ambient::store {

// Keeps each stream's IVs disjoint. Every domain counts seq from zero
enum class Domain : std::uint8_t {
    kAudio = 0,
    kTurns = 1,
    kNote = 2,
    kPatient = 3,
    kTranslation = 4,
    kLabel = 5,
    kSummary = 6,
    kReflection = 7,
    kGuidance = 8,
    // The added-document store: 9 under the store key, the rest under a document key
    kUploadKey = 9,
    kUploadName = 10,
    kUploadSection = 11,
    kUploadText = 12,
    kUploadVector = 13,
    kUploadBoxes = 14,
    kUploadFile = 15,
};

// AES-256-GCM per session. IV = domain + sequence, both authenticated.
// Destroying the key is the erase
class ChunkCipher {
   public:
    static ChunkCipher Generate();
    static ChunkCipher FromWrapped(std::span<const std::uint8_t> wrapped);

    // The key, DPAPI-protected for the current user, safe to persist
    std::vector<std::uint8_t> Wrapped(const wchar_t* description = L"ambient session key") const;

    // Another cipher's key sealed under this one, so a store key can hold each
    // document's key. seq must be fresh for every wrap
    std::vector<std::uint8_t> WrapKey(const ChunkCipher& key, std::string_view id,
                                      std::uint64_t seq) const;
    static ChunkCipher FromWrappedKey(const ChunkCipher& store, std::string_view id,
                                      std::uint64_t seq, std::span<const std::uint8_t> sealed);

    // A keyed hash of a file: the same file twice is one document, and the
    // value says nothing about the file to anyone without the key
    std::vector<std::uint8_t> Identity(const std::filesystem::path& file) const;

    // Returns ciphertext followed by the 16-byte tag
    std::vector<std::uint8_t> Seal(Domain domain, std::string_view session_id, std::uint64_t seq,
                                   std::span<const std::uint8_t> plain) const;

    // Throws if the payload fails authentication for any reason
    std::vector<std::uint8_t> Open(Domain domain, std::string_view session_id, std::uint64_t seq,
                                   std::span<const std::uint8_t> sealed) const;

    ChunkCipher(ChunkCipher&&) noexcept;
    ChunkCipher& operator=(ChunkCipher&&) noexcept;
    ~ChunkCipher();

   private:
    struct Impl;
    explicit ChunkCipher(std::unique_ptr<Impl> impl);

    std::unique_ptr<Impl> impl_;
};

}  // namespace ambient::store
