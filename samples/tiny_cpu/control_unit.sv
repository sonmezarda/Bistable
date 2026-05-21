module control_unit (
    input  logic [3:0] opcode,
    input  logic       zero_flag,
    input  logic       irq_pending,
    output logic [2:0] alu_op,
    output logic       reg_write,
    output logic       pc_load,
    output logic       mem_write,
    output logic       use_imm,
    output logic       halt_next
);
    assign pc_load = opcode == 4'h5 && zero_flag;
    assign halt_next = opcode == 4'hf;

    always_comb begin
        alu_op = 3'd0;
        reg_write = 1'b0;
        mem_write = 1'b0;
        use_imm = 1'b0;

        unique case (opcode)
            4'h0: begin
                alu_op = 3'd0;
                reg_write = 1'b1;
            end
            4'h1: begin
                alu_op = 3'd1;
                use_imm = 1'b1;
                reg_write = 1'b1;
            end
            4'h2: begin
                alu_op = 3'd2;
                reg_write = 1'b1;
            end
            4'h3: begin
                alu_op = 3'd3;
                use_imm = 1'b1;
                reg_write = 1'b1;
            end
            4'h4: begin
                mem_write = 1'b1;
            end
            4'h5: begin
            end
            4'hf: begin
            end
            default: begin
                alu_op = irq_pending ? 3'd4 : 3'd0;
            end
        endcase
    end
endmodule
