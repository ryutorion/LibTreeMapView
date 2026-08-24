#include <vector>
#include <string>
std::string beta_join(const std::vector<std::string>& parts) {
  std::string out;
  for (const auto& p : parts) { out += p; out += ","; }
  return out;
}
double beta_scale(double x) { return x * 3.14159; }
