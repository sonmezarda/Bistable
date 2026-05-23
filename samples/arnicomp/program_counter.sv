module program_counter (
    input  logic        clk,
    input  logic        rst_n,
    input  logic        count_en,
    input  logic        load_en,
    input  logic [15:0] load_data,
    output logic [15:0] paddr
);

    logic [15:0] pc_q;
    logic [15:0] pc_next;

    always_comb begin
        pc_next = pc_q;
        if (load_en)
            pc_next = load_data;
        else if (count_en)
            pc_next = pc_next + 16'd1;
    end

    reg_cell #(
        .W(16),
        .RESET_VALUE(16'h0000)
    ) program_reg (
        .clk(clk),
        .rst_n(rst_n),
        .we(load_en | count_en),
        .oe(1'b1),
        .d(pc_next),
        .out(pc_q)
    );

    assign paddr = pc_q;

endmodule
