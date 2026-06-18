using System.Collections.Generic;

namespace proiect_RISC.Models
{
    public static class DemoPrograms
    {
        public static List<(uint addr, string instr, string comment)> GetProgram(string key)
        {
            switch(key)
            {
                case "nota5_basic": return Demo_Nota5_Basic();
                case "nota5_hazard_raw": return Demo_Nota5_HazardRAW();
                case "nota5_forwarding": return Demo_Nota5_Forwarding();
                case "nota10_all": return Demo_Nota10_AllFeatures();
                case "nota10_loop": return Demo_Nota10_Loop();
                case "nota10_loadstore": return Demo_Nota10_LoadStore();
                default: return new List<(uint, string, string)>();
            }
        }

        public static Dictionary<string, string> GetProgramMenu() => new Dictionary<string, string>
        {
            ["nota5_basic"] = "Demo 1: Nota 5 — Cate o instructiune per clasa",
            ["nota5_hazard_raw"] = "Demo 2: Nota 5 — Hazard RAW fara forwarding (stall-uri)",
            ["nota5_forwarding"] = "Demo 3: Nota 5 — Hazard RAW rezolvat prin Forwarding",
            ["nota10_all"] = "Demo 4: Nota 10 — TOATE functionalitatile (recomandat pentru prezentare)",
            ["nota10_loop"] = "Demo 5: Nota 10 — Bucla cu BNE si hazarduri multiple",
            ["nota10_loadstore"] = "Demo 6: Nota 10 — LOAD/STORE cu hazard load-use"
        };

        private static List<(uint, string, string)> Demo_Nota5_Basic() => new List<(uint, string, string)>
        {
            (0x0100, "LDI R1, 10", "ALU IMM: R1 = 10  [CLASA LOAD-IMM]"),
            (0x0104, "LDI R2, 20", "ALU IMM: R2 = 20"),
            (0x0108, "NOP", "Separator — evita hazard RAW cu LDI"),
            (0x010C, "NOP", "Separator — pipeline fill"),
            (0x0110, "ADD R3, R1, R2", "ALU R-R-R: R3 = R1+R2 = 30  [CLASA ALU]"),
            (0x0114, "NOP", "Separator"),
            (0x0118, "NOP", "Separator"),
            (0x011C, "ST [R1], R3", "STORE: Mem[10] = 30  [CLASA STORE]"),
            (0x0120, "NOP", "Separator"),
            (0x0124, "NOP", "Separator"),
            (0x0128, "LD R4, [R1]", "LOAD: R4 = Mem[10] = 30  [CLASA LOAD]"),
            (0x012C, "NOP", "Separator"),
            (0x0130, "NOP", "Separator"),
            (0x0134, "JMP 0x013C", "[CLASA JMP] — salt la HALT"),
            (0x0138, "NOP", "Aceasta NOP nu se executa (dupa JMP)"),
            (0x013C, "HALT", "Oprire simulare")
        };

        private static List<(uint, string, string)> Demo_Nota5_HazardRAW() => new List<(uint, string, string)>
        {
            (0x0100, "LDI R1, 5", "R1 = 5"),
            (0x0104, "LDI R2, 3", "R2 = 3"),
            (0x0108, "NOP", "Asteptam LDI sa termine"),
            (0x010C, "NOP", "Asteptam LDI sa termine"),
            (0x0110, "ADD R3, R2, R1", "PRODUCE R3 = 8  [START HAZARD RAW]"),
            (0x0114, "SUB R4, R3, 1", "CONSUMA R3 — hazard RAW! (STALL x2 fara forwarding)"),
            (0x0118, "ADD R5, R4, R2", "Al doilea hazard: consuma R4 de la SUB"),
            (0x011C, "NOP", "Pipeline drain"),
            (0x0120, "NOP", "Pipeline drain"),
            (0x0124, "HALT", "Oprire — verifica R3=8, R4=7, R5=10")
        };

        private static List<(uint, string, string)> Demo_Nota5_Forwarding() => new List<(uint, string, string)>
        {
            (0x0100, "LDI R1, 5", "R1 = 5"),
            (0x0104, "LDI R2, 3", "R2 = 3"),
            (0x0108, "NOP", "Asteptam LDI"),
            (0x010C, "NOP", "Asteptam LDI"),
            (0x0110, "ADD R3, R2, R1", "PRODUCE R3 = 8"),
            (0x0114, "SUB R4, R3, 1", "CONSUMA R3 — FORWARDING EX->EX! (0 stall-uri)"),
            (0x0118, "ADD R5, R4, R2", "CONSUMA R4 — FORWARDING EX->EX!"),
            (0x011C, "NOP", "Pipeline drain"),
            (0x0120, "NOP", "Pipeline drain"),
            (0x0124, "HALT", "Oprire — verifica R3=8, R4=7, R5=10 (acelasi ca Demo 2)")
        };

        private static List<(uint, string, string)> Demo_Nota10_AllFeatures() => new List<(uint, string, string)>
        {
            (0x0100, "LDI R1, 10", "[1] CLASA LOAD-IMM: R1 = 10"),
            (0x0104, "LDI R2, 20", "[1] CLASA LOAD-IMM: R2 = 20"),
            (0x0108, "NOP", "Separator pentru LDI -> ADD"),
            (0x010C, "NOP", "Separator pentru LDI -> ADD"),
            (0x0110, "ADD R3, R1, R2", "[2] CLASA ALU: R3 = 30 (R1+R2)"),
            (0x0114, "SUB R4, R3, 1", "[3] HAZARD RAW R3: R4 = 29 (R3-1) -- FWD EX->EX"),
            (0x0118, "AND R5, R1, R2", "[4] CLASA ALU AND: R5 = 0 (10 AND 20 = 0)"),
            (0x011C, "OR  R5, R1, R2", "[4] CLASA ALU OR:  R5 = 30 (10 OR 20)"),
            (0x0120, "MUL R5, R3, R2", "[4] CLASA ALU MUL: R5 = 600 (30*20)"),
            (0x0124, "NOP", "Separator pentru MUL -> ST"),
            (0x0128, "ST [R1], R3", "[5] CLASA STORE: Mem[10] = 30"),
            (0x012C, "LD R6, [R1]", "[6] CLASA LOAD: R6 = Mem[10] = 30"),
            (0x0130, "ADDI R7, R6, 0", "[6] LOAD-USE HAZARD pe R6: 1 stall obligatoriu"),
            (0x0134, "BEQ R3, R6, 4", "[7] CLASA BRANCH: daca R3==R6 (30==30) sari"),
            (0x0138, "ADDI R7, R7, 99", "Aceasta NU se executa daca BEQ e luat"),
            (0x013C, "ADDI R7, R7, 1", "[8] CLASA ALU-IMM: R7 = R7 + 1 (flag branch luat)"),
            (0x0140, "JMP 0x0148", "[9] CLASA JMP: salt la HALT"),
            (0x0144, "ADDI R7, R7, 99", "Aceasta NU se executa (dupa JMP)"),
            (0x0148, "HALT", "HALT. Verifica: R1=10 R2=20 R3=30 R4=29 R6=30 R7=1")
        };

        private static List<(uint, string, string)> Demo_Nota10_Loop() => new List<(uint, string, string)>
        {
            (0x0100, "LDI R1, 1", "R1 = contor, incepe la 1"),
            (0x0104, "LDI R2, 6", "R2 = limita (6 = 1+2+3+4+5 se termina cand R1=6)"),
            (0x0108, "LDI R3, 0", "R3 = acumulator suma, incepe la 0"),
            (0x010C, "NOP", "Separator pentru LDI"),
            (0x0110, "NOP", "Separator pentru LDI"),
            (0x0114, "ADD R3, R3, R1", "LOOP: R3 += R1 (aduna contorul la suma)"),
            (0x0118, "ADDI R1, R1, 1", "LOOP: R1++ (incrementeaza contorul)"),
            (0x011C, "NOP", "Separator pentru BNE (evita hazard pe R1)"),
            (0x0120, "BNE R1, R2, -16", "LOOP: daca R1 != R2, sari la 0x0114 (offset=-16)"),
            (0x0124, "NOP", "Pipeline drain dupa BNE"),
            (0x0128, "NOP", "Pipeline drain"),
            (0x012C, "HALT", "HALT. Verifica: R1=6, R2=6, R3=15 (1+2+3+4+5)")
        };

        private static List<(uint, string, string)> Demo_Nota10_LoadStore() => new List<(uint, string, string)>
        {
            (0x0100, "LDI R1, 100", "R1 = adresa de memorie 100"),
            (0x0104, "LDI R2, 42", "R2 = valoarea 42"),
            (0x0108, "LDI R3, 7", "R3 = valoarea 7"),
            (0x010C, "NOP", "Separator"),
            (0x0110, "NOP", "Separator"),
            (0x0114, "ST [R1], R2", "Mem[100] = 42"),
            (0x0118, "ADDI R1, R1, 4", "R1 = 104 (adresa urmatoare)"),
            (0x011C, "ST [R1], R3", "Mem[104] = 7"),
            (0x0120, "ADDI R1, R1, -4", "R1 = 100 din nou"),
            (0x0124, "NOP", "Separator"),
            (0x0128, "LD R4, [R1]", "LOAD: R4 = Mem[100] = 42  [START LOAD-USE]"),
            (0x012C, "ADD R5, R4, R3", "LOAD-USE HAZARD: citeste R4 din LD (1 stall OBLIGATORIU)"),
            (0x0130, "ADDI R1, R1, 4", "Instructiune independenta — LD termina MEM in paralel"),
            (0x0134, "LD R6, [R1]", "LOAD: R6 = Mem[104] = 7"),
            (0x0138, "NOP", "1 NOP intre LD si ADD => MEM->EX forwarding"),
            (0x013C, "ADD R7, R6, R4", "MEM->EX FORWARDING: R6 vine din LD anterior"),
            (0x0140, "NOP", "Pipeline drain"),
            (0x0144, "NOP", "Pipeline drain"),
            (0x0148, "HALT", "HALT. Verifica: R4=42, R5=49(42+7), R6=7, R7=49(7+42)")
        };
    }
}