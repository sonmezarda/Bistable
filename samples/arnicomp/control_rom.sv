import control_pkg::*;

// Combinational control ROM — decodes the 8-bit instruction opcode into the
// full control word.  All timing-critical paths originate here.
module control_rom (
    input  logic [7:0]              instr,
    output control_pkg::ctrl_t      ctrl
);

    logic [1:0] instr_main_sel;
    logic [2:0] arr_sel;
    logic [2:0] source_sel;

    assign instr_main_sel = instr[7:6];
    assign arr_sel        = instr[5:3];
    assign source_sel     = instr[2:0];

    always_comb begin
        ctrl    = '0;
        ctrl.ce = 1'b1;

        case (instr_main_sel)
            2'b11: begin // LDL
                ctrl.im5_en = 1'b1;
                ctrl.we     = 1'b1;
                ctrl.dsel   = instr[5] == 0 ? 3'b000 : 3'b001;
            end

            2'b10: begin // MOV
                ctrl.we   = 1'b1;
                ctrl.oe   = 1'b1;
                ctrl.dsel = instr[5:3];
                ctrl.ssel = source_sel;
            end

            2'b01: begin // Arithmetic
                ctrl.sf   = 1'b1;
                ctrl.ssel = source_sel;
                ctrl.oe   = 1'b1;
                case (arr_sel)
                    3'b000: begin ctrl.ops = 2'b00; ctrl.accw = 1'b1; end                          // ADD
                    3'b001: begin ctrl.ops = 2'b00; ctrl.accw = 1'b1; ctrl.im3_low_en  = 1'b1; end // ADDI
                    3'b010: begin ctrl.ops = 2'b00; ctrl.accw = 1'b1; ctrl.sc          = 1'b1; end // ADC
                    3'b011: begin ctrl.ops = 2'b11; ctrl.accw = 1'b1; end                          // NOT
                    3'b100: begin ctrl.ops = 2'b00; ctrl.accw = 1'b1; ctrl.sn          = 1'b1; end // SUB
                    3'b101: begin ctrl.ops = 2'b00; ctrl.accw = 1'b1; ctrl.im3_low_en  = 1'b1;
                                  ctrl.sn = 1'b1; end                                              // SUBI
                    3'b110: begin ctrl.ops = 2'b00; ctrl.accw = 1'b1; ctrl.sc = 1'b1;
                                  ctrl.sn = 1'b1; end                                              // SBC
                    3'b111: begin ctrl.ops = 2'b00; ctrl.sn = 1'b1; end                            // CMP
                    default: ;
                endcase
            end

            2'b00: begin // Other opcodes
                case (arr_sel)
                    3'b001: begin // XOR
                        ctrl.ops  = 2'b10; ctrl.accw = 1'b1; ctrl.sf = 1'b1;
                        ctrl.ssel = source_sel; ctrl.oe = 1'b1;
                    end
                    3'b010: begin // AND
                        ctrl.ops  = 2'b01; ctrl.accw = 1'b1; ctrl.sf = 1'b1;
                        ctrl.ssel = source_sel; ctrl.oe = 1'b1;
                    end
                    3'b011: begin ctrl.jmp = 1'b1; end // JMP
                    3'b100: begin // PUSH
                        ctrl.ssel        = source_sel; ctrl.oe = 1'b1;
                        ctrl.we          = 1'b1;       ctrl.dsel = 3'b111;
                        ctrl.inc_dec_sel = 1'b0;       ctrl.sp_sel = 1'b1;
                    end
                    3'b101: begin // POP
                        ctrl.dsel        = instr[2:0]; ctrl.oe = 1'b1;
                        ctrl.we          = 1'b1;       ctrl.ssel = 3'b111;
                        ctrl.inc_dec_sel = 1'b1;       ctrl.sp_sel = 1'b1;
                    end
                    3'b110: begin ctrl.im3_high_en = 1'b1; ctrl.dsel = 3'b000; ctrl.we = 1'b1; end // LDH Ra
                    3'b111: begin ctrl.im3_high_en = 1'b1; ctrl.dsel = 3'b001; ctrl.we = 1'b1; end // LDH Rd
                    3'b000: begin // Misc
                        case (instr[2:0])
                            3'b000: ;                                                      // NOP
                            3'b001: ctrl.ce = 1'b0;                                        // HLT
                            3'b010: begin ctrl.inc_dec_sel = 1'b0; ctrl.inc_mar = 1'b1; end // INC 1
                            3'b011: begin ctrl.inc_dec_sel = 1'b0; ctrl.inc_mar = 1'b1; end // INC 2
                            3'b100: begin ctrl.inc_dec_sel = 1'b1; ctrl.inc_mar = 1'b1; end // DEC 1
                            3'b101: begin ctrl.inc_dec_sel = 1'b1; ctrl.inc_mar = 1'b1; end // DEC 2
                            3'b110: begin ctrl.jmp = 1'b1; ctrl.jgt    = 1'b1; end          // JGT
                            3'b111: begin ctrl.jmp = 1'b1; ctrl.set_lr = 1'b1; end          // JAL
                            default: ;
                        endcase
                    end
                    default: ;
                endcase
            end
        endcase
    end

endmodule
