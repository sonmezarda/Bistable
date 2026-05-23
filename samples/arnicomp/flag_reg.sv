module flag_reg (
    input  logic clk,
    input  logic rst_n,
    input  logic we,
    input  logic z_f_in,
    input  logic n_f_in,
    input  logic c_f_in,
    input  logic v_f_in,
    output logic z_f_out,
    output logic n_f_out,
    output logic c_f_out,
    output logic v_f_out
);

    reg_cell #(.W(4)) flag_register (
        .clk(clk),
        .rst_n(rst_n),
        .we(we),
        .oe(1'b1),
        .d({z_f_in, n_f_in, c_f_in, v_f_in}),
        .out({z_f_out, n_f_out, c_f_out, v_f_out})
    );

endmodule
