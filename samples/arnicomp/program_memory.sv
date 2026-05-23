// Synchronous-read instruction ROM.
// Defaults to a short demo program: LDL Ra,3 | LDL Rd,5 | ADD | MOV Ra,ACC | HLT
module program_memory #(
    parameter int    ADDR_WIDTH = 16,
    parameter int    DATA_WIDTH = 8,
    parameter int    MEM_SIZE   = 64,
    parameter string MEM_FILE   = ""
)(
    input  logic                  clk,
    input  logic [ADDR_WIDTH-1:0] addr,
    output logic [DATA_WIDTH-1:0] data
);

    localparam int MEM_ADDR_BITS = $clog2(MEM_SIZE);

    logic [DATA_WIDTH-1:0] mem [0:MEM_SIZE-1];

    initial begin
        // Default demo program
        mem[0] = 8'hC3; // LDL Ra, 3
        mem[1] = 8'hE5; // LDL Rd, 5
        mem[2] = 8'h40; // ADD Rd, Ra  -> ACC = 8
        mem[3] = 8'h83; // MOV Ra, ACC -> Ra  = 8
        mem[4] = 8'h88; // MOV Rd, Ra  -> Rd  = 8
        mem[5] = 8'h01; // HLT
        for (int i = 6; i < MEM_SIZE; i++) mem[i] = 8'h00;
        if (MEM_FILE != "") $readmemh(MEM_FILE, mem);
    end

    always_ff @(posedge clk) begin
        data <= mem[addr[MEM_ADDR_BITS-1:0]];
    end

endmodule
