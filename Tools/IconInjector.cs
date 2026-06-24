using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal static class NativeMethods
{
    public static readonly IntPtr RT_ICON = new IntPtr(3);
    public static readonly IntPtr RT_GROUP_ICON = new IntPtr(14);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool UpdateResource(
        IntPtr hUpdate,
        IntPtr lpType,
        IntPtr lpName,
        ushort wLanguage,
        byte[] lpData,
        uint cbData);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);
}

internal sealed class IconInjector
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct GRPICONDIRENTRY
    {
        public byte bWidth;
        public byte bHeight;
        public byte bColorCount;
        public byte bReserved;
        public ushort wPlanes;
        public ushort wBitCount;
        public uint dwBytesInRes;
        public ushort nID;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct ICONDIR
    {
        public ushort idReserved;
        public ushort idType;
        public ushort idCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct ICONDIRENTRY
    {
        public byte bWidth;
        public byte bHeight;
        public byte bColorCount;
        public byte bReserved;
        public ushort wPlanes;
        public ushort wBitCount;
        public uint dwBytesInRes;
        public uint dwImageOffset;
    }

    public static void InjectIcon(string exePath, string iconPath)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException("EXE nicht gefunden.", exePath);
        if (!File.Exists(iconPath))
            throw new FileNotFoundException("Icon nicht gefunden.", iconPath);

        var iconBytes = File.ReadAllBytes(iconPath);
        var iconDir = BytesToStructure<ICONDIR>(iconBytes, 0);
        if (iconDir.idReserved != 0 || iconDir.idType != 1)
            throw new InvalidDataException("Ungueltige ICO-Datei.");

        var images = new List<byte[]>();
        var entries = new List<ICONDIRENTRY>();
        var offset = Marshal.SizeOf(typeof(ICONDIR));

        for (var i = 0; i < iconDir.idCount; i++)
        {
            var entry = BytesToStructure<ICONDIRENTRY>(iconBytes, offset);
            entries.Add(entry);
            var image = new byte[entry.dwBytesInRes];
            Buffer.BlockCopy(iconBytes, (int)entry.dwImageOffset, image, 0, image.Length);
            images.Add(image);
            offset += Marshal.SizeOf(typeof(ICONDIRENTRY));
        }

        var hUpdate = NativeMethods.BeginUpdateResource(exePath, false);
        if (hUpdate == IntPtr.Zero)
            throw new InvalidOperationException("BeginUpdateResource fehlgeschlagen: " + Marshal.GetLastWin32Error());

        try
        {
            var iconBaseId = 1;
            var groupEntries = new GRPICONDIRENTRY[entries.Count];

            for (var i = 0; i < entries.Count; i++)
            {
                var iconId = new IntPtr(iconBaseId + i);
                if (!NativeMethods.UpdateResource(
                        hUpdate,
                        NativeMethods.RT_ICON,
                        iconId,
                        0,
                        images[i],
                        (uint)images[i].Length))
                {
                    throw new InvalidOperationException("UpdateResource ICON fehlgeschlagen: " + Marshal.GetLastWin32Error());
                }

                groupEntries[i] = new GRPICONDIRENTRY
                {
                    bWidth = entries[i].bWidth,
                    bHeight = entries[i].bHeight,
                    bColorCount = entries[i].bColorCount,
                    bReserved = entries[i].bReserved,
                    wPlanes = entries[i].wPlanes,
                    wBitCount = entries[i].wBitCount,
                    dwBytesInRes = entries[i].dwBytesInRes,
                    nID = (ushort)(iconBaseId + i)
                };
            }

            var groupDir = new ICONDIR { idReserved = 0, idType = 1, idCount = (ushort)groupEntries.Length };
            var groupSize = Marshal.SizeOf(typeof(ICONDIR)) + groupEntries.Length * Marshal.SizeOf(typeof(GRPICONDIRENTRY));
            var groupBytes = new byte[groupSize];
            WriteStructure(groupDir, groupBytes, 0);

            var groupOffset = Marshal.SizeOf(typeof(ICONDIR));
            for (var i = 0; i < groupEntries.Length; i++)
            {
                WriteStructure(groupEntries[i], groupBytes, groupOffset);
                groupOffset += Marshal.SizeOf(typeof(GRPICONDIRENTRY));
            }

            if (!NativeMethods.UpdateResource(
                    hUpdate,
                    NativeMethods.RT_GROUP_ICON,
                    new IntPtr(1),
                    0,
                    groupBytes,
                    (uint)groupBytes.Length))
            {
                throw new InvalidOperationException("UpdateResource GROUP_ICON fehlgeschlagen: " + Marshal.GetLastWin32Error());
            }

            if (!NativeMethods.EndUpdateResource(hUpdate, false))
                throw new InvalidOperationException("EndUpdateResource fehlgeschlagen: " + Marshal.GetLastWin32Error());

            hUpdate = IntPtr.Zero;
        }
        finally
        {
            if (hUpdate != IntPtr.Zero)
                NativeMethods.EndUpdateResource(hUpdate, true);
        }
    }

    private static T BytesToStructure<T>(byte[] bytes, int offset) where T : struct
    {
        var size = Marshal.SizeOf(typeof(T));
        if (offset + size > bytes.Length)
            throw new InvalidDataException("ICO-Eintrag unvollstaendig.");

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return (T)Marshal.PtrToStructure(new IntPtr(handle.AddrOfPinnedObject().ToInt64() + offset), typeof(T));
        }
        finally
        {
            handle.Free();
        }
    }

    private static void WriteStructure<T>(T structure, byte[] buffer, int offset) where T : struct
    {
        var size = Marshal.SizeOf(typeof(T));
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            Marshal.StructureToPtr(structure, new IntPtr(handle.AddrOfPinnedObject().ToInt64() + offset), false);
        }
        finally
        {
            handle.Free();
        }
    }

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Verwendung: IconInjector.exe <exe> <ico>");
            return 2;
        }

        try
        {
            InjectIcon(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
            Console.WriteLine("Icon gesetzt: " + args[0]);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
