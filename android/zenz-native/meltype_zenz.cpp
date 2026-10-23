// SPDX-License-Identifier: GPL-3.0-or-later
#include "meltype_zenz.h"
#include "llama.h"
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace {
using Clock = std::chrono::steady_clock;
struct Engine {
    llama_model * model = nullptr;
    llama_context * context = nullptr;
    std::mutex gate;
    std::atomic<bool> cancelled{false};
    Clock::time_point deadline;
    ~Engine() {
        if (context) llama_free(context);
        if (model) llama_model_free(model);
    }
};
bool abort_generation(void * opaque) {
    auto * engine = static_cast<Engine *>(opaque);
    return engine->cancelled.load(std::memory_order_relaxed) || Clock::now() >= engine->deadline;
}
std::once_flag backend_once;
}

void * meltype_zenz_create(const char * model_path, int threads) {
    if (!model_path || !*model_path) return nullptr;
    try {
        std::call_once(backend_once, [] { llama_backend_init(); });
        auto engine = std::make_unique<Engine>();
        auto model_params = llama_model_default_params();
        model_params.n_gpu_layers = 0;
        engine->model = llama_model_load_from_file(model_path, model_params);
        if (!engine->model) return nullptr;
        auto context_params = llama_context_default_params();
        context_params.n_ctx = 512;
        context_params.n_batch = 512;
        context_params.n_ubatch = 128;
        context_params.n_threads = std::clamp(threads, 1, 4);
        context_params.n_threads_batch = context_params.n_threads;
        context_params.no_perf = true;
        engine->context = llama_init_from_model(engine->model, context_params);
        if (!engine->context) return nullptr;
        llama_set_abort_callback(engine->context, abort_generation, engine.get());
        return engine.release();
    } catch (...) { return nullptr; }
}

int meltype_zenz_generate(void * handle, const char * prompt, const char * grammar, char * output, int capacity, int timeout_ms) {
    if (output && capacity > 0) output[0] = 0;
    if (!handle || !prompt || !*prompt || !output || capacity < 2 || timeout_ms <= 0) return -1;
    auto * engine = static_cast<Engine *>(handle);
    std::lock_guard<std::mutex> lock(engine->gate);
    engine->cancelled.store(false, std::memory_order_relaxed);
    engine->deadline = Clock::now() + std::chrono::milliseconds(std::min(timeout_ms, 30000));
    try {
        const auto prompt_length = std::strlen(prompt);
        if (prompt_length > 2048) return -2;
        auto * vocab = llama_model_get_vocab(engine->model);
        int count = -llama_tokenize(vocab, prompt, (int)prompt_length, nullptr, 0, true, true);
        if (count <= 0 || count > 384) return -2;
        std::vector<llama_token> tokens(count);
        if (llama_tokenize(vocab, prompt, (int)prompt_length, tokens.data(), count, true, true) != count) return -2;
        llama_kv_cache_clear(engine->context);
        auto batch = llama_batch_get_one(tokens.data(), count);
        std::unique_ptr<llama_sampler, decltype(&llama_sampler_free)> sampler(
            llama_sampler_chain_init(llama_sampler_chain_default_params()), llama_sampler_free);
        if (!sampler) return -3;
        if (grammar && *grammar) {
            if (std::strlen(grammar) > 16384) return -2;
            auto * constraint = llama_sampler_init_grammar(vocab, grammar, "root");
            if (!constraint) return -3;
            llama_sampler_chain_add(sampler.get(), constraint);
        }
        llama_sampler_chain_add(sampler.get(), llama_sampler_init_greedy());
        std::string generated;
        for (int step = 0; step < 128; ++step) {
            if (abort_generation(engine)) return -4;
            if (llama_decode(engine->context, batch) != 0) return -4;
            if (abort_generation(engine)) return -4;
            llama_token token = llama_sampler_sample(sampler.get(), engine->context, -1);
            if (llama_vocab_is_eog(vocab, token)) {
                if (generated.empty()) return -5;
                std::memcpy(output, generated.data(), generated.size());
                output[generated.size()] = 0;
                return (int)generated.size();
            }
            char piece[256];
            int length = llama_token_to_piece(vocab, token, piece, sizeof(piece), 0, false);
            if (length < 0 || generated.size() + (size_t)length >= (size_t)capacity) return -6;
            generated.append(piece, length);
            tokens[0] = token;
            batch = llama_batch_get_one(tokens.data(), 1);
        }
        return -7;
    } catch (...) { return -3; }
}

void meltype_zenz_cancel(void * handle) {
    if (handle) static_cast<Engine *>(handle)->cancelled.store(true, std::memory_order_relaxed);
}
void meltype_zenz_destroy(void * handle) { delete static_cast<Engine *>(handle); }
