// Prints the units the ingest would store for a PDF, one JSON object per
// line, for the gates the plan runs on real documents
#include <cstdint>
#include <cstdio>
#include <exception>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <nlohmann/json.hpp>
#include <vector>

#include "adapters/guidance/ingest_host.hpp"
#include "core/document_units.hpp"

int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::fprintf(stderr, "usage: ambient_units <document.pdf>\n");
        return 1;
    }
    std::ifstream file(argv[1], std::ios::binary);
    const std::vector<std::uint8_t> bytes((std::istreambuf_iterator<char>(file)),
                                          std::istreambuf_iterator<char>());
    const auto host = std::filesystem::path(argv[0]).parent_path() / "ambient_ingest_host.exe";
    try {
        auto pages = ambient::guidance::IngestHost(host).Extract(bytes);
        for (const auto& unit : ambient::guidance::UnitsFromPages(pages)) {
            const nlohmann::json row{{"page", unit.page},
                                     {"number", unit.number},
                                     {"section", unit.section},
                                     {"text", unit.text}};
            std::printf("%s\n", row.dump().c_str());
        }
    } catch (const std::exception& e) {
        std::fprintf(stderr, "%s\n", e.what());
        return 2;
    }
    return 0;
}
