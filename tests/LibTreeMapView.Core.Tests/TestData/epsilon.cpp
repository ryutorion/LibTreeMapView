// /bigobj ????????ANON_OBJECT_HEADER_BIGOBJ ?????????????
int epsilon_one(int x) { return x + 1; }
int epsilon_two(int x) { return x * 3; }
static const int kValues[32] = {1};
int epsilon_pick(int i) { return kValues[i & 31]; }
