; RISC Pipeline Simulator - Example Program
; Format: [address:] OPCODE operands ; comment
; Address este optional - daca lipseste, se incrementeaza automat cu 4

0x0100: LDI R1, 10      ; Incarca R1 = 10
0x0104: LDI R2, 20      ; Incarca R2 = 20
0x0108: ADD R3, R1, R2  ; R3 = R1 + R2 = 30
0x010C: SUBI R4, R3, 5   ; R4 = R3 - 5 = 25
0x0110: MUL R5, R4, R2  ; R5 = R4 * R2 = 500
0x0114: ST [R1], R5     ; Mem[10] = 500
0x0118: LD R6, [R1]     ; R6 = Mem[10] = 500
0x011C: BEQ R5, R6, 8   ; Daca R5 == R6, sari cu 8 bytes
0x0120: ADDI R7, R7, 99 ; Aceasta nu se executa (BEQ e luat)
0x0124: ADDI R7, R7, 1  ; R7 = 1 (branch target)
0x0128: HALT            ; Oprire
