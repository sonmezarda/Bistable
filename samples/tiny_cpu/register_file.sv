module register_file (
    input  logic       clk,
    input  logic       rst_n,
    input  logic       enable,
    input  logic       reg_write,
    input  logic       pc_load,
    input  logic [7:0] alu_result,
    input  logic [7:0] immediate,
    output logic [7:0] pc,
    output logic [7:0] acc
);
    always_ff @(posedge clk or negedge rst_n) begin
        if (!rst_n) begin
            pc <= 8'h00;
            acc <= 8'h00;
        end else if (enable) begin
            pc <= pc_load ? immediate : pc + 8'h01;
            if (reg_write) begin
                acc <= alu_result;
            end
        end
    end
endmodule
