#include <iostream>
#include <string>

int main() {
    std::string line;
    while (std::getline(std::cin, line)) {
        if (line == "quit") {
            return 0;
        }

        std::cout << "{\"status\":\"worker-template\",\"message\":\"generated Verilator worker not implemented yet\"}" << std::endl;
    }

    return 0;
}
