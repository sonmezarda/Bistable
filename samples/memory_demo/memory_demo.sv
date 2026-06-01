// P3-11: tiny RAM module that exercises the memory probe path. The single
// always_ff block latches `din` into mem[addr] on the rising edge when `we`
// is high; `dout` is a continuous read of mem[addr]. 16 cells x 8 bits.
module memory_demo (
    input  logic        clk,
    input  logic        we,
    input  logic [3:0]  addr,
    input  logic [7:0]  din,
    output logic [7:0]  dout
);
    logic [7:0] mem [0:15];

    always_ff @(posedge clk) begin
        if (we) mem[addr] <= din;
    end

    assign dout = mem[addr];
endmodule
