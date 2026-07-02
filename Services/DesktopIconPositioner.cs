using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluidDecks.Core.Win32;

namespace FluidDecks.Services
{
    public static class DesktopIconPositioner
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, int nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint PROCESS_VM_OPERATION = 0x0008;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint PROCESS_VM_WRITE = 0x0020;
        private const uint MEM_COMMIT = 0x1000;
        private const uint MEM_RESERVE = 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;

        private const uint LVM_GETITEMCOUNT = 0x1004;
        private const uint LVM_GETITEMPOSITION = 0x1010;
        private const uint LVM_GETITEMTEXTW = 0x1073;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }

        // For 64-bit Explorer
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct LVITEM64
        {
            public uint mask;
            public int iItem;
            public int iSubItem;
            public uint state;
            public uint stateMask;
            public IntPtr pszText;
            public int cchTextMax;
            public int iImage;
            public IntPtr lParam;
            public int iIndent;
            public int iGroupId;
            public uint cColumns;
            public IntPtr puColumns;
            public IntPtr piColFmt;
            public int iGroup;
        }

        private static IntPtr GetDesktopListViewHandle()
        {
            IntPtr progman = FindWindow("Progman", "Program Manager");
            IntPtr defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                // If not found in Progman, look in WorkerW
                IntPtr workerW = IntPtr.Zero;
                do
                {
                    workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                    defView = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                } while (defView == IntPtr.Zero && workerW != IntPtr.Zero);
            }

            if (defView != IntPtr.Zero)
            {
                return FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
            }
            return IntPtr.Zero;
        }

        public static Dictionary<string, POINT> GetDesktopIconPositions()
        {
            var positions = new Dictionary<string, POINT>(StringComparer.OrdinalIgnoreCase);

            IntPtr hWndListView = GetDesktopListViewHandle();
            if (hWndListView == IntPtr.Zero)
                return positions;

            GetWindowThreadProcessId(hWndListView, out uint processId);
            IntPtr hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, processId);
            if (hProcess == IntPtr.Zero)
                return positions;

            int itemCount = (int)SendMessage(hWndListView, LVM_GETITEMCOUNT, IntPtr.Zero, IntPtr.Zero);
            if (itemCount == 0)
            {
                // Close process handle and return
                CloseHandle(hProcess);
                return positions;
            }

            // Allocate memory for LVITEM, POINT, and string buffer
            IntPtr allocatedMemory = VirtualAllocEx(hProcess, IntPtr.Zero, 4096, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
            if (allocatedMemory == IntPtr.Zero)
            {
                CloseHandle(hProcess);
                return positions;
            }

            IntPtr ptAddress = allocatedMemory;
            IntPtr stringAddress = allocatedMemory + Marshal.SizeOf(typeof(POINT));
            IntPtr lvItemAddress = stringAddress + 512; // 512 bytes for string

            for (int i = 0; i < itemCount; i++)
            {
                // Get Position
                SendMessage(hWndListView, LVM_GETITEMPOSITION, (IntPtr)i, ptAddress);
                
                POINT pt;
                IntPtr bytesRead;
                IntPtr ptrPointLocal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(POINT)));
                ReadProcessMemory(hProcess, ptAddress, ptrPointLocal, Marshal.SizeOf(typeof(POINT)), out bytesRead);
                pt = (POINT)Marshal.PtrToStructure(ptrPointLocal, typeof(POINT));
                Marshal.FreeHGlobal(ptrPointLocal);

                // Get Text
                LVITEM64 lvItem = new LVITEM64();
                lvItem.mask = 0x0001; // LVIF_TEXT
                lvItem.iItem = i;
                lvItem.iSubItem = 0;
                lvItem.pszText = stringAddress;
                lvItem.cchTextMax = 255;

                IntPtr ptrLvItemLocal = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(LVITEM64)));
                Marshal.StructureToPtr(lvItem, ptrLvItemLocal, false);
                WriteProcessMemory(hProcess, lvItemAddress, ptrLvItemLocal, Marshal.SizeOf(typeof(LVITEM64)), out IntPtr bytesWritten);
                Marshal.FreeHGlobal(ptrLvItemLocal);

                SendMessage(hWndListView, LVM_GETITEMTEXTW, (IntPtr)i, lvItemAddress);

                byte[] stringBuffer = new byte[512];
                IntPtr ptrStringLocal = Marshal.AllocHGlobal(512);
                ReadProcessMemory(hProcess, stringAddress, ptrStringLocal, 512, out bytesRead);
                Marshal.Copy(ptrStringLocal, stringBuffer, 0, 512);
                Marshal.FreeHGlobal(ptrStringLocal);

                string iconName = Encoding.Unicode.GetString(stringBuffer).TrimEnd('\0');

                if (!string.IsNullOrEmpty(iconName))
                {
                    positions[iconName] = pt;
                }
            }

            VirtualFreeEx(hProcess, allocatedMemory, 0, MEM_RELEASE);
            CloseHandle(hProcess);

            return positions;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
