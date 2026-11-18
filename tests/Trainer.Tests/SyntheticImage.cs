using System.Text;
using TLOUTrainer.Core;

namespace Trainer.Tests;

/// <summary>
/// Builds a miniature "tlou-i.exe" containing the same engine shapes the analyser
/// looks for: the ammo functions, the debug-menu registrations and a handful of
/// RIP-relative flag reads. Nothing is copied from the real game.
/// </summary>
internal static class SyntheticImage
{
    public const int Infinite = 0x4000;
    public const int Bottomless = 0x4001;
    public const int NoReload = 0x4004;
    public const int Health = 0x4020;
    public const int WeaponHealth = 0x4030;
    public const int Craftables = 0x4031;
    public const int Crafting = 0x4032;
    public const int Scratch = 0x4800;
    public const int ShivFunction = 0x1900;
    public const int ShivBaseCall = 0x1919;
    public const int ShivUpgradeCall = 0x192A;
    public const int ShivFunctionEnd = 0x192E;

    public static GameImage Build()
    {
        var bytes = new byte[0x6000];
        Array.Fill(bytes, (byte)0xCC);
        var sections = new List<ImageSection>
        {
            new(".text", 0x1000, 0x2000, true, false),
            new(".rdata", 0x3000, 0x1000, false, false),
            new(".data", 0x4000, 0x1000, false, true),
            new(".pdata", 0x5000, 0x1000, false, false)
        };
        Array.Clear(bytes, 0x4000, 0x1000); // flags start as zero
        Array.Clear(bytes, 0x5000, 0x1000);

        void Put(int rva, params byte[] data) => data.CopyTo(bytes, rva);
        byte[] Disp(int rva, int length, int target) => BitConverter.GetBytes(target - (rva + length));

        // bool IsInfiniteAmmoActive(): debug flag OR gameplay modifier.
        Put(0x1100, 0x32, 0xC9);
        Put(0x1102, 0x38, 0x0D); Put(0x1104, Disp(0x1102, 6, Infinite));
        Put(0x1108, 0x75, 0x0F);
        Put(0x110A, 0x48, 0x8B, 0x05); Put(0x110D, Disp(0x110A, 7, 0x4008));
        Put(0x1111, 0x38, 0x88, 0x68, 0x49, 0x00, 0x00);
        Put(0x1117, 0x74, 0x02, 0xB1, 0x01, 0x8A, 0xC1, 0xC3);

        // Reload helper: three neighbouring debug bools.
        Put(0x1200, 0x40, 0x38, 0x3D); Put(0x1203, Disp(0x1200, 7, NoReload));
        Put(0x1207, 0x40, 0x8A, 0xCF);
        Put(0x120A, 0x74, 0x02, 0xB1, 0x01);
        Put(0x120E, 0x40, 0x38, 0x3D); Put(0x1211, Disp(0x120E, 7, 0x4002));
        Put(0x1215, 0x75, 0x0C);
        Put(0x1217, 0x40, 0x38, 0x3D); Put(0x121A, Disp(0x1217, 7, Bottomless));

        // Debug-menu registrations: lea r9,[flag]; lea r8,[callback]; lea rdx,[name]; call.
        void Register(int rva, int flag, int callback, int label)
        {
            Put(rva, 0x4C, 0x8D, 0x0D); Put(rva + 3, Disp(rva, 7, flag));
            Put(rva + 7, 0x48, 0x89, 0x7C, 0x24, 0x28);
            Put(rva + 12, 0x4C, 0x8D, 0x05); Put(rva + 15, Disp(rva + 12, 7, callback));
            Put(rva + 19, 0x48, 0x89, 0x7C, 0x24, 0x20);
            Put(rva + 24, 0x48, 0x8D, 0x15); Put(rva + 27, Disp(rva + 24, 7, label));
            Put(rva + 31, 0x48, 0x8B, 0xCD);
            Put(rva + 34, 0xE8); Put(rva + 35, Disp(rva + 34, 5, 0x1500));
        }
        Register(0x1300, Infinite, 0x1400, 0x3000);
        Register(0x1330, Health, 0x1410, 0x3020);

        // Secondary bool toggles use the shorter registration shape.
        void RegisterSimple(int rva, int flag, int label)
        {
            Put(rva, 0x4C, 0x8D, 0x05); Put(rva + 3, Disp(rva, 7, flag));
            Put(rva + 7, 0x48, 0x8D, 0x15); Put(rva + 10, Disp(rva + 7, 7, label));
            Put(rva + 14, 0x48, 0x8B, 0xCD, 0xE8);
        }
        RegisterSimple(0x1360, WeaponHealth, 0x3040);
        RegisterSimple(0x1380, Craftables, 0x3060);
        RegisterSimple(0x13A0, Crafting, 0x3080);

        Encoding.ASCII.GetBytes("Infinite Ammo\0").CopyTo(bytes, 0x3000);
        Encoding.ASCII.GetBytes("Invincible Player\0").CopyTo(bytes, 0x3020);
        Encoding.ASCII.GetBytes("Infinite Weapon Health\0").CopyTo(bytes, 0x3040);
        Encoding.ASCII.GetBytes("Infinite Craftables\0").CopyTo(bytes, 0x3060);
        Encoding.ASCII.GetBytes("Infinite Crafting\0").CopyTo(bytes, 0x3080);
        Encoding.ASCII.GetBytes("t1x-shiv\0").CopyTo(bytes, 0x30A0);
        Encoding.ASCII.GetBytes("t1x-shiv-upgrade\0").CopyTo(bytes, 0x30C0);

        // A miniature shiv-only function with separately validated inventory calls.
        Put(ShivFunction, 0x48, 0x8D, 0x35); Put(ShivFunction + 3, Disp(ShivFunction, 7, 0x30C0));
        Put(ShivFunction + 7, 0x4C, 0x8D, 0x35); Put(ShivFunction + 10, Disp(ShivFunction + 7, 7, 0x30A0));
        Put(0x190E, 0x4C, 0x8B, 0x90, 0x20, 0x01, 0x00, 0x00); // mov r10,[rax+120h]
        Put(0x1915, 0x45, 0x8D, 0x41, 0x01);                   // lea r8d,[r9+1]
        Put(ShivBaseCall, 0x41, 0xFF, 0xD2);
        Put(0x191C, 0x4C, 0x8B, 0x88, 0x20, 0x01, 0x00, 0x00); // mov r9,[rax+120h]
        Put(0x1923, 0x45, 0x8D, 0x47, 0x01);                    // lea r8d,[r15+1]
        Put(0x1927, 0x48, 0x8B, 0xD7);                          // mov rdx,rdi
        Put(ShivUpgradeCall, 0x41, 0xFF, 0xD1);
        Put(0x192D, 0xC3);
        Put(0x5000, BitConverter.GetBytes(ShivFunction));
        Put(0x5004, BitConverter.GetBytes(ShivFunctionEnd));

        // Cmp/test/movzx reads of the flags, in the exact instruction forms the engine uses.
        void CmpByte(int rva, int target)
        {
            Put(rva, 0x38, 0x0D); Put(rva + 2, Disp(rva, 6, target));
        }
        void CmpImm(int rva, int target)
        {
            Put(rva, 0x80, 0x3D); Put(rva + 2, Disp(rva, 7, target)); Put(rva + 6, 0x00);
        }

        // The function at 0x1102 is the first infinite-ammo read; this is the second.
        CmpByte(0x1600, Infinite);
        // One read already lives in the reload helper at 0x1217; these are the other four.
        CmpImm(0x1620, Bottomless);
        for (int i = 1; i < 4; i++) CmpByte(0x1620 + i * 0x10, Bottomless);
        // Optional melee/crafting toggles.
        CmpByte(0x1580, WeaponHealth);
        CmpImm(0x1590, WeaponHealth);
        CmpByte(0x15A0, Craftables);
        CmpByte(0x15B0, Crafting);
        CmpByte(0x15C0, Crafting);
        CmpByte(0x1700, Health);
        CmpByte(0x1710, Health);
        CmpByte(0x1720, Health);
        // bool getter: movzx eax, byte ptr [rip+disp32]
        Put(0x1760, 0x0F, 0xB6, 0x05); Put(0x1763, Disp(0x1760, 7, Health));

        return new GameImage(bytes, sections);
    }
}
