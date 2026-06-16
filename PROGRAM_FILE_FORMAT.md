# Format Fișiere Program RISC

## Descriere

Simulatorul acceptă fișiere text (.asm, .txt) care conțin instrucțiuni RISC în format assembly.

## Format General

```
[adresa:] OPCODE operanzi ; comentariu
```

- **adresa** (opțional): Adresa în format hexazecimal (0x0100) sau zecimal. Dacă lipsește, se auto-incrementează cu 4.
- **OPCODE**: Numele instrucțiunii (vezi lista mai jos)
- **operanzi**: Registre (R0-R15) sau valori imediate
- **comentariu** (opțional): Text după `;`

## Exemple de Linii

```assembly
0x0100: LDI R1, 10      ; Incarca R1 = 10
ADD R2, R1, R1          ; R2 = R1 + R1 (adresa auto: 0x0104)
        SUB R3, R2, 5   ; R3 = R2 - 5 (adresa auto: 0x0108)
; Aceasta linie este ignorata (comentariu)
        HALT            ; Oprire program
```

## Set de Instrucțiuni Suportat

### ALU (Registre)
- `ADD Rd, Rs1, Rs2` - Rd = Rs1 + Rs2
- `SUB Rd, Rs1, Rs2` - Rd = Rs1 - Rs2
- `MUL Rd, Rs1, Rs2` - Rd = Rs1 * Rs2
- `AND Rd, Rs1, Rs2` - Rd = Rs1 & Rs2
- `OR  Rd, Rs1, Rs2` - Rd = Rs1 | Rs2
- `XOR Rd, Rs1, Rs2` - Rd = Rs1 ^ Rs2
- `SHL Rd, Rs1, Rs2` - Rd = Rs1 << Rs2
- `SHR Rd, Rs1, Rs2` - Rd = Rs1 >> Rs2

### ALU (Imediat)
- `ADDI Rd, Rs1, Imm` - Rd = Rs1 + Imm
- `SUBI Rd, Rs1, Imm` - Rd = Rs1 - Imm
- `ANDI Rd, Rs1, Imm` - Rd = Rs1 & Imm
- `ORI  Rd, Rs1, Imm` - Rd = Rs1 | Imm

### LOAD/STORE
- `LD  Rd, [Rs1]`     - Rd = Memory[Rs1]
- `LDI Rd, Imm`       - Rd = Imm
- `ST  [Rs1], Rs2`    - Memory[Rs1] = Rs2

### BRANCH/JUMP
- `JMP Imm`           - PC = Imm
- `BEQ Rs1, Rs2, Imm` - if (Rs1 == Rs2) PC = PC + Imm
- `BNE Rs1, Rs2, Imm` - if (Rs1 != Rs2) PC = PC + Imm
- `BGT Rs1, Rs2, Imm` - if (Rs1 > Rs2) PC = PC + Imm
- `BLT Rs1, Rs2, Imm` - if (Rs1 < Rs2) PC = PC + Imm

### PSEUDO-INSTRUCȚIUNI
- `NOP`               - No operation
- `HALT`              - Oprește simulatorul
- `MOV Rd, Rs1`       - Rd = Rs1 (echivalent ADDI Rd, Rs1, 0)

## Valori Imediate

- **Zecimal**: `10`, `-5`, `255`
- **Hexazecimal**: `0x0A`, `0xFF`, `100h`

## Registre

- **R0-R15**: 16 registre generale
- **R0**: Întotdeauna 0 (read-only, ca în MIPS/RISC-V)

## Exemplu Complet

Vezi `example_program.asm` pentru un program demonstrativ.

## Utilizare în Simulator

1. **File → Load Program...** - Selectează fișierul .asm sau .txt
2. Programul se încarcă în tabelul "Program Memory"
3. Apasă **▶ Next Clock** pentru execuție pas-cu-pas sau **⏭ Run to End** pentru execuție completă
