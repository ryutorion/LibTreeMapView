namespace demo { namespace math {
int add(int a, int b) { return a + b; }
int mul(int a, int b) { return a * b; }
int poly(int x) { int s = 0; for (int i = 0; i < 8; ++i) s = s * x + i; return s; }
} }
extern "C" __declspec(dllexport) int entry(int x) { return demo::math::poly(x) + demo::math::add(x, 1) + demo::math::mul(x, 2); }
