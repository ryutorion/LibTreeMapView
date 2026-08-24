#pragma section(".averylongsectionname", read, write)
int gamma_twice(int x) { return x * 2; }
static const double kTable[8] = {1,2,3,4,5,6,7,8};
double gamma_pick(int i) { return kTable[i & 7]; }
__declspec(allocate(".averylongsectionname")) int g_marker[16] = {7};
