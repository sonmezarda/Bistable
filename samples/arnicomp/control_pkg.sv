package control_pkg;

  typedef struct packed {
    logic        jgt;         // jump greater than
    logic        inc_mar;     // update full 16-bit MAR
    logic [1:0]  ops;         // ALU op select
    logic        sn;          // set negative (subtract)
    logic        ce;          // PC count enable
    logic        jmp;         // jump active
    logic        sc;          // carry into ALU from flag register

    logic        set_lr;      // capture PC into link register
    logic        we;          // destination write enable
    logic        accw;        // ACC write enable
    logic [2:0]  dsel;        // destination select
    logic        sf;          // set flags

    logic        im3_low_en;  // use 3-bit low immediate
    logic        im3_high_en; // use 3-bit high immediate
    logic        inc_dec_sel; // 0 = increment, 1 = decrement
    logic        sp_sel;      // address from stack pointer
    logic [2:0]  ssel;        // source select
    logic        oe;          // bus output enable
    logic        im5_en;      // use 5-bit immediate

  } ctrl_t;

  localparam int CTRL_W = $bits(ctrl_t);

endpackage
