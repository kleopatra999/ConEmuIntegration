﻿//
// Copyright 2016 David Roller
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ConEmuIntegration.ToolWindow
{
    internal sealed class ConEmuMacro
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string libname);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        private delegate int FConsoleMain3(int anWorkMode, string asCommandLine);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
        private delegate int FGuiMacro(string asWhere, string asMacro, out IntPtr bstrResult);

        private string m_LibraryPath;
        private IntPtr m_ConEmuHandle;
        private FConsoleMain3 m_fnConsoleMain3;
        private FGuiMacro m_fnGuiMacro;

        public ConEmuMacro(string asLibrary)
        {
            m_ConEmuHandle = IntPtr.Zero;
            m_fnConsoleMain3 = null;
            m_fnGuiMacro = null;
            m_LibraryPath = asLibrary;
            LoadConEmuDll(asLibrary);
        }

        ~ConEmuMacro()
        {
            UnloadConEmuDll();
        }

        private void ExecuteLegacy(string asWhere, string asMacro)
        {
            try
            {
                if (m_ConEmuHandle == IntPtr.Zero)
                {
                    return;
                }

                string cmdLine = " -GuiMacro";
                if (string.IsNullOrEmpty(asWhere) == false)
                {
                    cmdLine += ":" + asWhere;
                }
                else
                {
                    cmdLine += " " + asMacro;
                }

                Environment.SetEnvironmentVariable("ConEmuMacroResult", null);
                m_fnConsoleMain3.Invoke(3, cmdLine);
            }
            catch (Exception error)
            {
                ExceptionMessageBox box = new ExceptionMessageBox();
                box.SetException(error);
                box.ShowDialog();
            }
        }

        private void ExecuteHelper(string asWhere, string asMacro)
        {
            try
            {
                if (m_fnGuiMacro != null)
                {
                    IntPtr bstrPtr = IntPtr.Zero;
                    int result = m_fnGuiMacro.Invoke(asWhere, asMacro, out bstrPtr);
                    if (result != 133 /*CERR_GUIMACRO_SUCCEEDED*/ || result != 134 /*CERR_GUIMACRO_FAILED*/)
                    {
                        return; // Sucess
                    }

                    ExceptionMessageBox box = new ExceptionMessageBox();
                    box.SetException("Invoke GuiMacro method failed",
                        "Failure while invoke the GuiMacro of the conemu library" + Environment.NewLine +
                        "Result code: " + result);
                    box.ShowDialog();

                    if (bstrPtr != IntPtr.Zero)
                    {
                        Marshal.FreeBSTR(bstrPtr);
                    }
                }
                else
                {
                    ExecuteLegacy(asWhere, asMacro);
                }
            }
            catch (Exception error)
            {
                ExceptionMessageBox box = new ExceptionMessageBox();
                box.SetException(error);
                box.ShowDialog();
            }
        }

        public void Execute(string asWhere, string asMacro)
        {
            try
            {
                if (m_ConEmuHandle == IntPtr.Zero)
                {
                    return;
                }

                new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    ExecuteHelper(asWhere, asMacro);
                }).Start();
            }
            catch (Exception error)
            {
                ExceptionMessageBox box = new ExceptionMessageBox();
                box.SetException(error);
                box.ShowDialog();
            }
        }

        private void LoadConEmuDll(string asLibrary)
        {
            try
            {
                if (m_ConEmuHandle != IntPtr.Zero)
                {
                    return;
                }

                m_ConEmuHandle = LoadLibrary(asLibrary);
                if (m_ConEmuHandle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    ExceptionMessageBox box = new ExceptionMessageBox();
                    box.SetException("Unable to load the conemu library", 
                        "Failure while load the conemu library" + Environment.NewLine +
                        "Library path: " + asLibrary + Environment.NewLine +
                        "Last error code: " + error);
                    box.ShowDialog();
                    return;
                }

                const string fnNameOld = "ConsoleMain3";
                IntPtr ptrConsoleMain = GetProcAddress(m_ConEmuHandle, fnNameOld);

                const string fnNameNew = "GuiMacro";
                IntPtr ptrGuiMacro = GetProcAddress(m_ConEmuHandle, fnNameNew);

                if (ptrConsoleMain == IntPtr.Zero && ptrGuiMacro == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    ExceptionMessageBox box = new ExceptionMessageBox();
                    box.SetException("Unable to load the conemu library",
                        "Failure while getting the addresses of the methods in the conemu library" + 
                        Environment.NewLine +
                        "Library path: " + asLibrary + Environment.NewLine +
                        "Methods: ConsoleMain3, GuiMacro" + Environment.NewLine +
                        "Last error code: " + error);
                    box.ShowDialog();

                    UnloadConEmuDll();
                    return;
                }

                m_fnGuiMacro = (FGuiMacro)Marshal.GetDelegateForFunctionPointer(ptrGuiMacro, typeof(FGuiMacro));
                m_fnConsoleMain3 = (FConsoleMain3)Marshal.GetDelegateForFunctionPointer(ptrConsoleMain, typeof(FConsoleMain3));
            }
            catch (Exception error)
            {
                UnloadConEmuDll();

                ExceptionMessageBox box = new ExceptionMessageBox();
                box.SetException(error);
                box.ShowDialog();
            }
        }

        private void UnloadConEmuDll()
        {
            if (m_ConEmuHandle != IntPtr.Zero)
            {
                FreeLibrary(m_ConEmuHandle);
                m_ConEmuHandle = IntPtr.Zero;
            }
        }
    }
}
