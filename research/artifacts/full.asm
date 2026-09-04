0x00401000  0020                     add byte ptr [eax], ah
0x00401002  0419                     add al, 0x19
0x00401004  40                       inc eax
0x00401005  0000                     add byte ptr [eax], al
0x00401007  000c2f                   add byte ptr [edi + ebp], cl
0x0040100a  4a                       dec edx
0x0040100b  0000                     add byte ptr [eax], al
0x0040100d  2018                     and byte ptr [eax], bl
0x0040100f  2f                       das 
0x00401010  4a                       dec edx
0x00401011  0000                     add byte ptr [eax], al
0x00401013  1ce8                     sbb al, 0xe8
0x00401015  014b00                   add dword ptr [ebx], ecx
0x00401018  0020                     add byte ptr [eax], ah
0x0040101a  fc                       cld 
0x0040101b  334a00                   xor ecx, dword ptr [edx]
0x0040101e  0020                     add byte ptr [eax], ah
0x00401020  8436                     test byte ptr [esi], dh
0x00401022  4a                       dec edx
0x00401023  0000                     add byte ptr [eax], al
0x00401025  207741                   and byte ptr [edi + 0x41], dh
0x00401028  4a                       dec edx
0x00401029  0000                     add byte ptr [eax], al
0x0040102b  003448                   add byte ptr [eax + ecx*2], dh
0x0040102e  4a                       dec edx
0x0040102f  0000                     add byte ptr [eax], al
0x00401031  00c8                     add al, cl
0x00401033  4a                       dec edx
0x00401034  4a                       dec edx
0x00401035  0000                     add byte ptr [eax], al
0x00401037  051c674a00               add eax, 0x4a671c
0x0040103c  0004d0                   add byte ptr [eax + edx*8], al
0x0040103f  5f                       pop edi
0x00401040  4a                       dec edx
0x00401041  0000                     add byte ptr [eax], al
0x00401043  0a20                     or ah, byte ptr [eax]
0x00401045  894a00                   mov dword ptr [edx], ecx
0x00401048  000a                     add byte ptr [edx], cl
0x0040104a  38914a00000a             cmp byte ptr [ecx + 0xa00004a], dl
0x00401050  54                       push esp
0x00401051  8c4a00                   mov word ptr [edx], cs
0x00401054  000a                     add byte ptr [edx], cl
0x00401056  18964a00000a             sbb byte ptr [esi + 0xa00004a], dl
0x0040105c  b485                     mov ah, 0x85
0x0040105e  4a                       dec edx
0x0040105f  0000                     add byte ptr [eax], al
0x00401061  01e8                     add eax, ebp
0x00401063  9d                       popfd 
0x00401064  4a                       dec edx
0x00401065  0000                     add byte ptr [eax], al
0x00401067  03aca44a000002           add ebp, dword ptr [esp + 0x200004a]
0x0040106e  28aa4a000003             sub byte ptr [edx + 0x300004a], ch
0x00401074  10ac4a00000644           adc byte ptr [edx + ecx*2 + 0x44060000], ch
0x0040107b  b14a                     mov cl, 0x4a
0x0040107d  0000                     add byte ptr [eax], al
0x0040107f  0114b3                   add dword ptr [ebx + esi*4], edx
0x00401082  4a                       dec edx
0x00401083  0000                     add byte ptr [eax], al
0x00401085  01f4                     add esp, esi
0x00401087  b44a                     mov ah, 0x4a
0x00401089  0000                     add byte ptr [eax], al
0x0040108b  0078b6                   add byte ptr [eax - 0x4a], bh
0x0040108e  4a                       dec edx
0x0040108f  0000                     add byte ptr [eax], al
0x00401091  14e8                     adc al, 0xe8
0x00401093  bb4a000015               mov ebx, 0x1500004a
0x00401098  b8bd4a0000               mov eax, 0x4abd
0x0040109d  0010                     add byte ptr [eax], dl
0x0040109f  be4a000020               mov esi, 0x2000004a
0x004010a4  a1e34a0000               mov eax, dword ptr [0x4ae3]
0x004010a9  1f                       pop ds
0x004010aa  3402                     xor al, 2
0x004010ac  4b                       dec ebx
0x004010ad  0000                     add byte ptr [eax], al
0x004010af  1e                       push ds
0x004010b0  1c2c                     sbb al, 0x2c
0x004010b2  4a                       dec edx
0x004010b3  0000                     add byte ptr [eax], al
0x004010b5  1e                       push ds
0x004010b6  60                       pushal 
0x004010b7  16                       push ss
0x004010b8  40                       inc eax
0x004010b9  0000                     add byte ptr [eax], al
0x004010bb  1e                       push ds
0x004010bc  90                       nop 
0x004010bd  ca4800                   retf 0x48
0x004010c0  001e                     add byte ptr [esi], bl
0x004010c2  d4c9                     aam 0xc9
0x004010c4  48                       dec eax
0x004010c5  0000                     add byte ptr [eax], al
0x004010c7  1e                       push ds
0x004010c8  68b5470000               push 0x47b5
0x004010cd  1e                       push ds
0x004010ce  bc37490000               mov esp, 0x4937
0x004010d3  1e                       push ds
0x004010d4  d8b74700001e             fdiv dword ptr [edi + 0x1e000047]
0x004010da  0c34                     or al, 0x34
0x004010dc  49                       dec ecx
0x004010dd  0000                     add byte ptr [eax], al
0x004010df  1e                       push ds
0x004010e0  58                       pop eax
0x004010e1  b548                     mov ch, 0x48
0x004010e3  0000                     add byte ptr [eax], al
0x004010e5  1e                       push ds
0x004010e6  b0c0                     mov al, 0xc0
0x004010e8  47                       inc edi
0x004010e9  0000                     add byte ptr [eax], al
0x004010eb  1e                       push ds
0x004010ec  80cc47                   or ah, 0x47
0x004010ef  0000                     add byte ptr [eax], al
0x004010f1  1e                       push ds
0x004010f2  f4                       hlt 
0x004010f3  40                       inc eax
0x004010f4  49                       dec ecx
0x004010f5  0000                     add byte ptr [eax], al
0x004010f7  1e                       push ds
0x004010f8  ac                       lodsb al, byte ptr [esi]
0x004010f9  d14900                   ror dword ptr [ecx], 1
0x004010fc  001e                     add byte ptr [esi], bl
0x004010fe  70b4                     jo 0x4010b4
0x00401100  48                       dec eax
0x00401101  0000                     add byte ptr [eax], al
0x00401103  1e                       push ds
0x00401104  48                       dec eax
0x00401105  a6                       cmpsb byte ptr [esi], byte ptr es:[edi]
0x00401106  48                       dec eax
0x00401107  0000                     add byte ptr [eax], al
0x00401109  1e                       push ds
0x0040110a  94                       xchg esp, eax
0x0040110b  c8480000                 enter 0x48, 0
0x0040110f  1e                       push ds
0x00401110  18dd                     sbb ch, bl
0x00401112  47                       inc edi
0x00401113  0000                     add byte ptr [eax], al
0x00401115  1e                       push ds
0x00401116  84b34100001e             test byte ptr [ebx + 0x1e000041], dh
0x0040111c  80b84700001ea8           cmp byte ptr [eax + 0x1e000047], 0xa8
0x00401123  b448                     mov ah, 0x48
0x00401125  0000                     add byte ptr [eax], al
0x00401127  1e                       push ds
0x00401128  ec                       in al, dx
0x00401129  c6430000                 mov byte ptr [ebx], 0
0x0040112d  1e                       push ds
0x0040112e  10b14700001e             adc byte ptr [ecx + 0x1e000047], dh
0x00401134  48                       dec eax
0x00401135  cc                       int3 
0x00401136  47                       inc edi
0x00401137  0000                     add byte ptr [eax], al
0x00401139  1e                       push ds
0x0040113a  04cc                     add al, 0xcc
0x0040113c  41                       inc ecx
0x0040113d  0000                     add byte ptr [eax], al
0x0040113f  1e                       push ds
0x00401140  ac                       lodsb al, byte ptr [esi]
0x00401141  c44700                   les eax, ptr [edi]
0x00401144  001e                     add byte ptr [esi], bl
0x00401146  3c1e                     cmp al, 0x1e
0x00401148  43                       inc ebx
0x00401149  0000                     add byte ptr [eax], al
0x0040114b  1e                       push ds
0x0040114c  dcfc                     fdiv st(4), st(0)
0x0040114e  44                       inc esp
0x0040114f  0000                     add byte ptr [eax], al
0x00401151  1e                       push ds
0x00401152  10b84700001e             adc byte ptr [eax + 0x1e000047], bh
0x00401158  d8b54700001e             fdiv dword ptr [ebp + 0x1e000047]
