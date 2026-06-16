using System;
using System.Collections.Generic;
using System.Linq;

namespace proiect_RISC.Models
{
    public class InstructionParser
    {
        public RISCInstruction Parse(string rawText, uint address)
        {
            var instr = new RISCInstruction
            {
                Address = address,
                RawText = rawText?.Trim() ?? "",
                Comment = "",
                Opcode = Opcode.UNKNOWN,
                Class = InstructionClass.UNKNOWN
            };

            if (string.IsNullOrWhiteSpace(rawText))
            {
                instr.Opcode = Opcode.NOP;
                instr.Class = InstructionClass.NOP;
                return instr;
            }

            var parts = rawText.Split(';');
            if (parts.Length > 1) instr.Comment = parts[1].Trim();
            var code = parts[0].Trim();

            var tokens = code.Split(new char[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) return instr;

            string opcodeStr = tokens[0].ToUpperInvariant();
            string operandStr = tokens.Length > 1 ? tokens[1] : "";

            if (!Enum.TryParse<Opcode>(opcodeStr, out var opcode)) return instr;
            instr.Opcode = opcode;

            var operands = operandStr.Split(',').Select(o => o.Trim()).ToArray();

            switch (opcode)
            {
                case Opcode.ADD: case Opcode.SUB: case Opcode.MUL: case Opcode.AND:
                case Opcode.OR: case Opcode.XOR: case Opcode.SHL: case Opcode.SHR:
                    instr.Class = InstructionClass.ALU;
                    if (operands.Length >= 3)
                    {
                        instr.Rd = ParseRegister(operands[0]);
                        instr.Rs1 = ParseRegister(operands[1]);
                        instr.Rs2 = ParseRegister(operands[2]);
                    }
                    break;
                case Opcode.ADDI: case Opcode.SUBI: case Opcode.ANDI: case Opcode.ORI:
                    instr.Class = InstructionClass.ALUI;
                    if (operands.Length >= 3)
                    {
                        instr.Rd = ParseRegister(operands[0]);
                        instr.Rs1 = ParseRegister(operands[1]);
                        instr.Imm = ParseImmediate(operands[2]);
                    }
                    break;
                case Opcode.LD:
                    instr.Class = InstructionClass.LOAD;
                    if (operands.Length >= 2)
                    {
                        instr.Rd = ParseRegister(operands[0]);
                        instr.Rs1 = ParseIndirectRegister(operands[1]); 
                    }
                    break;
                case Opcode.LDI:
                    instr.Class = InstructionClass.LOAD;
                    if (operands.Length >= 2)
                    {
                        instr.Rd = ParseRegister(operands[0]);
                        instr.Imm = ParseImmediate(operands[1]);
                    }
                    break;
                case Opcode.ST:
                    instr.Class = InstructionClass.STORE;
                    if (operands.Length >= 2)
                    {
                        instr.Rs1 = ParseIndirectRegister(operands[0]); 
                        instr.Rs2 = ParseRegister(operands[1]);
                    }
                    break;
                case Opcode.JMP:
                    instr.Class = InstructionClass.JUMP;
                    if (operands.Length >= 1) instr.Imm = ParseImmediate(operands[0]);
                    break;
                case Opcode.BEQ: case Opcode.BNE: case Opcode.BGT: case Opcode.BLT:
                    instr.Class = InstructionClass.BRANCH;
                    if (operands.Length >= 3)
                    {
                        instr.Rs1 = ParseRegister(operands[0]);
                        instr.Rs2 = ParseRegister(operands[1]);
                        instr.Imm = ParseImmediate(operands[2]);
                    }
                    break;
                case Opcode.NOP:
                    instr.Class = InstructionClass.NOP;
                    instr.Rd = 0; instr.Rs1 = 0; instr.Imm = 0;
                    break;
                case Opcode.HALT:
                    instr.Class = InstructionClass.HALT;
                    break;
                case Opcode.MOV:
                    instr.Class = InstructionClass.ALUI;
                    instr.Opcode = Opcode.ADDI; 
                    if (operands.Length >= 2)
                    {
                        instr.Rd = ParseRegister(operands[0]);
                        instr.Rs1 = ParseRegister(operands[1]);
                        instr.Imm = 0;
                    }
                    break;
                default:
                    instr.Class = InstructionClass.UNKNOWN;
                    break;
            }
            return instr;
        }

        public List<RISCInstruction> ParseProgram(IEnumerable<(uint address, string text)> rows)
        {
            return rows.Where(r => !string.IsNullOrWhiteSpace(r.text))
                       .Select(r => Parse(r.text, r.address))
                       .ToList();
        }

        private int ParseRegister(string token)
        {
            token = token.Trim().ToUpperInvariant();
            if (token.StartsWith("R") && int.TryParse(token.Substring(1), out int idx) && idx >= 0 && idx < RegisterFile.Count)
                return idx;
            return -1;
        }

        private int ParseIndirectRegister(string token)
        {
            return ParseRegister(token.Trim().TrimStart('[').TrimEnd(']'));
        }

        private int ParseImmediate(string token)
        {
            token = token.Trim().ToUpperInvariant();
            try
            {
                if (token.StartsWith("0X")) return Convert.ToInt32(token.Substring(2), 16);
                if (token.EndsWith("H")) return Convert.ToInt32(token.TrimEnd('H'), 16);
                if (int.TryParse(token, out int dec)) return dec;
            }
            catch { }
            return 0;
        }

        public (bool isValid, string errorMessage) Validate(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText)) return (true, "");
            var instr = Parse(rawText, 0);
            if (instr.Opcode == Opcode.UNKNOWN) return (false, $"Opcode necunoscut: '{rawText.Split(' ')[0]}'");
            if (instr.Class == InstructionClass.ALU && (instr.Rd < 0 || instr.Rs1 < 0 || instr.Rs2 < 0)) return (false, "Format ALU invalid: OPCODE Rd, Rs1, Rs2");
            if (instr.Class == InstructionClass.LOAD && instr.Opcode == Opcode.LD && (instr.Rd < 0 || instr.Rs1 < 0)) return (false, "Format LD invalid: LD Rd, [Rs]");
            if (instr.Class == InstructionClass.STORE && (instr.Rs1 < 0 || instr.Rs2 < 0)) return (false, "Format ST invalid: ST [Rs1], Rs2");
            if (instr.Class == InstructionClass.BRANCH && (instr.Rs1 < 0 || instr.Rs2 < 0)) return (false, "Format Branch invalid: BEQ Rs1, Rs2, Imm");
            return (true, "");
        }
    }
}