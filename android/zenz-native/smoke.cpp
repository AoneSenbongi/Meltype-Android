// SPDX-License-Identifier: GPL-3.0-or-later
#include "meltype_zenz.h"
#include <chrono>
#include <cstdio>
#include <cstring>

int main(int argc, char ** argv) {
    if (argc != 2) return 64;
    if (meltype_zenz_create("", 2) != nullptr) return 1;
    if (meltype_zenz_generate(nullptr, "test", nullptr, nullptr, 0, 100) >= 0) return 2;
    auto start = std::chrono::steady_clock::now();
    void * handle = meltype_zenz_create(argv[1], 2);
    if (!handle) return 3;
    auto loaded = std::chrono::steady_clock::now();
    char output[2048] = {};
    // U+EE00 + ニホンゴ + U+EE01, encoded explicitly for all host compilers.
    const char * prompt = "\xEE\xB8\x80\xE3\x83\x8B\xE3\x83\x9B\xE3\x83\xB3\xE3\x82\xB4\xEE\xB8\x81";
    int result = meltype_zenz_generate(handle, prompt, nullptr, output, sizeof(output), 30000);
    auto generated = std::chrono::steady_clock::now();
    std::printf("load_ms=%lld generate_ms=%lld result=%d output=%s\n",
        (long long)std::chrono::duration_cast<std::chrono::milliseconds>(loaded-start).count(),
        (long long)std::chrono::duration_cast<std::chrono::milliseconds>(generated-loaded).count(), result, output);
    if (result <= 0 || std::strcmp(output, "\xE6\x97\xA5\xE6\x9C\xAC\xE8\xAA\x9E") != 0) {
        meltype_zenz_destroy(handle); return 4;
    }
    if (meltype_zenz_generate(handle, prompt, nullptr, output, 1, 30000) >= 0) {
        meltype_zenz_destroy(handle); return 5;
    }
    const char * grammar = "root ::= \"\xE6\x97\xA5\xE6\x9C\xAC\xE8\xAA\x9E\"";
    if (meltype_zenz_generate(handle, prompt, grammar, output, sizeof(output), 30000) != 9 ||
        std::strcmp(output, "\xE6\x97\xA5\xE6\x9C\xAC\xE8\xAA\x9E") != 0) {
        meltype_zenz_destroy(handle); return 6;
    }
    if (meltype_zenz_generate(handle, prompt, grammar, output, sizeof(output), 1) != -4 || output[0] != 0) {
        meltype_zenz_destroy(handle); return 7;
    }
    meltype_zenz_destroy(handle);
    meltype_zenz_destroy(nullptr);
    std::puts("PASS: model load, Japanese generation, grammar, deadline, invalid handles and output bound");
    return 0;
}
