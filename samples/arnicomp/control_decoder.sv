module control_decoder (
    input  logic [7:0]              instr_in,
    output control_pkg::ctrl_t      ctrl_pkg_out,
    output logic [2:0]              jmp_sel,
    output logic                    reg_a_we,
    output logic                    reg_b_we,
    output logic                    reg_d_we,
    output logic                    marl_we,
    output logic                    marh_we,
    output logic                    prl_we,
    output logic                    prh_we,
    output logic                    mem_we,
    output logic                    acc_we
);

    control_rom control_rom_i (
        .instr(instr_in),
        .ctrl(ctrl_pkg_out)
    );

    assign jmp_sel = instr_in[2:0];
    assign acc_we  = ctrl_pkg_out.accw;

    always_comb begin
        reg_a_we = 1'b0;
        reg_d_we = 1'b0;
        reg_b_we = 1'b0;
        marl_we  = 1'b0;
        marh_we  = 1'b0;
        prl_we   = 1'b0;
        prh_we   = 1'b0;
        mem_we   = 1'b0;

        if (ctrl_pkg_out.we) begin
            case (ctrl_pkg_out.dsel)
                3'b000: reg_a_we = 1'b1;
                3'b001: reg_d_we = 1'b1;
                3'b010: reg_b_we = 1'b1;
                3'b011: marl_we  = 1'b1;
                3'b100: marh_we  = 1'b1;
                3'b101: prl_we   = 1'b1;
                3'b110: prh_we   = 1'b1;
                3'b111: mem_we   = 1'b1;
                default: ;
            endcase
        end
    end

endmodule
