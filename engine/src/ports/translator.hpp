#pragma once

#include <functional>
#include <string>
#include <vector>

namespace clinicavt::translate {

// Streams partials, returns the translation, throws on failure. Cancel
// interrupts from another thread
class ITranslator {
   public:
    using Progress = std::function<void(const std::string&)>;

    virtual ~ITranslator() = default;

    virtual std::vector<std::string> Languages() = 0;

    // Starts any slow loading in the background so the first Translate is
    // warm. Safe to call repeatedly
    virtual void Prepare() {}

    // Frees the model once no translation holds it. The next Prepare or
    // Translate loads it again
    virtual void Release() {}

    virtual std::string Translate(const std::string& text, const std::string& language,
                                  const Progress& progress) = 0;

    virtual void Cancel() {}
};

}  // namespace clinicavt::translate
