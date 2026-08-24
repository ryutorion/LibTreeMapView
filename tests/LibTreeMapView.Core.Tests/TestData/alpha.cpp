#include <cstring>
int g_initialized[64] = {1,2,3};
int g_uninitialized[256];
const char* g_message = "hello from alpha";
int alpha_add(int a, int b) { return a + b; }
int alpha_sum(const int* v, int n) { int s = 0; for (int i = 0; i < n; ++i) s += v[i]; return s; }
