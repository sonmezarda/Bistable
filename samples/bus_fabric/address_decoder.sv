module address_decoder (
    input  logic [7:0] addr,
    input  logic       read,
    input  logic       write,
    output logic       sel_gpio,
    output logic       sel_timer
);
    logic access;

    assign access = read | write;
    assign sel_gpio = access & (addr[7:4] == 4'h0);
    assign sel_timer = access & (addr[7:4] == 4'h1);
endmodule
