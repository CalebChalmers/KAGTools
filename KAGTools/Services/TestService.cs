﻿using KAGTools.Data;
using Serilog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace KAGTools.Services
{
    public class TestService : ITestService
    {
        private const string DefaultRconPassword = "test";
        private const int TcprMaxRetries = 100;
        private const double TcprRetryWaitSeconds = 1.0;

        // Relevant file paths
        public string KagExecutablePath { get; set; }
        public string AutoConfigPath { get; set; }
        public string SoloAutoStartScriptPath { get; set; }
        public string ClientAutoStartScriptPath { get; set; }
        public string ServerAutoStartScriptPath { get; set; }

        public TestService(string kagExecutablePath, string autoConfigPath, string soloAutoStartScriptPath, string clientAutoStartScriptPath, string serverAutoStartScriptPath)
        {
            KagExecutablePath = kagExecutablePath;
            AutoConfigPath = autoConfigPath;
            SoloAutoStartScriptPath = soloAutoStartScriptPath;
            ClientAutoStartScriptPath = clientAutoStartScriptPath;
            ServerAutoStartScriptPath = serverAutoStartScriptPath;
        }

        public bool TestSolo()
        {
            Log.Information("Starting solo test");

            try
            {
                return StartKagProcess(SoloAutoStartScriptPath) != null;
            }
            catch (Win32Exception ex)
            {
                Log.Error(ex, "Failed to start solo test process");
                return false;
            }
        }

        public async Task<bool> TestMultiplayerAsync(int port, bool syncClientServerClosing)
        {
            Log.Information("Starting multiplayer test");

            // Start the server process
            Process serverProcess;

            try
            {
                Log.Information("TestMultiplayer: Starting test server process");
                serverProcess = StartKagProcess(ServerAutoStartScriptPath);
                if (serverProcess == null) return false;
            }
            catch (Win32Exception ex)
            {
                Log.Error(ex, "TestMultiplayer: Failed to start test server process");
                return false;
            }

            // Wait until we can connect to the server's TCPR port and start the client process
            if (await TryConnectToServerTcprPortAsync(serverProcess, port))
            {
                Log.Information("TestMultiplayer: Starting test client process");
                var clientProcess = StartKagProcess(ClientAutoStartScriptPath);

                if (clientProcess != null)
                {
                    if (syncClientServerClosing)
                    {
                        // If either the client or server exits, close the other
                        clientProcess.EnableRaisingEvents = true;
                        serverProcess.EnableRaisingEvents = true;
                        clientProcess.Exited += (s, e) => CloseIfStillRunning(serverProcess);
                        serverProcess.Exited += (s, e) => CloseIfStillRunning(clientProcess);
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if (serverProcess.HasExited)
                {
                    Log.Warning("TestMultiplayer: Test server closed before we could connect");
                }
                else
                {
                    Log.Warning("TestMultiplayer: Failed all tries to connect to the test server");
                }
                return false;
            }
        }

        public bool TryFixMultiplayerAutoConfigProperties(IConfigService configService)
        {
            var tcprProperty = new BoolConfigProperty("sv_tcpr", false);
            var passwordProperty = new StringConfigProperty("sv_rconpassword", "");
            configService.ReadConfigProperties(AutoConfigPath, tcprProperty, passwordProperty);

            if (!tcprProperty.Value || passwordProperty.Value == "")
            {
                Log.Information("TCPR is disabled or password isn't set. Writing updated properties");

                if (passwordProperty.Value == "")
                {
                    passwordProperty.Value = DefaultRconPassword;
                }

                tcprProperty.Value = true;

                return configService.WriteConfigProperties(AutoConfigPath, tcprProperty, passwordProperty);
            }

            return true;
        }

        private async Task<bool> TryConnectToServerTcprPortAsync(Process serverProcess, int port)
        {
            using (var tcpClient = new TcpClient(AddressFamily.InterNetwork))
            {
                for (int i = 0; i < TcprMaxRetries; i++)
                {
                    if (serverProcess.HasExited) break;

                    await Task.Delay(TimeSpan.FromSeconds(TcprRetryWaitSeconds));

                    try
                    {
                        Log.Information("TryConnectToServerTcprPort: Attempting to connect to test server ({Tries})", i + 1);
                        await tcpClient.ConnectAsync("localhost", port);

                        if (tcpClient.Connected)
                        {
                            Log.Information("TryConnectToServerTcprPort: Sucessfully connected to test server");
                            return true;
                        }
                    }
                    catch (SocketException) { }
                }
            }
            return false;
        }

        private void CloseIfStillRunning(Process process)
        {
            if (process != null && !process.HasExited)
            {
                process.CloseMainWindow();
            }
        }

        private Process StartKagProcess(string autostart)
        {
            return Process.Start(new ProcessStartInfo()
            {
                FileName = KagExecutablePath,
                Arguments = $"noautoupdate nolauncher autostart \"{autostart}\"",
                WorkingDirectory = Path.GetDirectoryName(KagExecutablePath)
            });
        }
    }
}
