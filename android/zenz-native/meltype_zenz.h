// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once
#ifdef _WIN32
#define MT_ZENZ_API __declspec(dllexport)
#else
#define MT_ZENZ_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif
MT_ZENZ_API void * meltype_zenz_create(const char * model_path, int threads);
// Positive UTF-8 byte count only after EOS; negative means no usable result.
// Calls on a handle are serialized. Cancel may be called from another thread.
MT_ZENZ_API int meltype_zenz_generate(void * handle, const char * prompt, const char * grammar, char * output, int capacity, int timeout_ms);
MT_ZENZ_API void meltype_zenz_cancel(void * handle);
// Caller must stop submitting jobs and join its worker before destroying.
MT_ZENZ_API void meltype_zenz_destroy(void * handle);
#ifdef __cplusplus
}
#endif
