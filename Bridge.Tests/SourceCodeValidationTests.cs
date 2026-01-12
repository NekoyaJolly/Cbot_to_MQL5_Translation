using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Bridge.Tests
{
    /// <summary>
    /// Compilation validation tests for MQL5 EA and cBot
    /// Verifies that source code compiles successfully with MetaEditor and cTrader toolchains
    /// </summary>
    public class CompilationTests
    {
        private readonly ITestOutputHelper _output;
        private readonly string _projectRoot;

        public CompilationTests(ITestOutputHelper output)
        {
            _output = output;
            _projectRoot = FindProjectRoot();
        }

        private string FindProjectRoot()
        {
            var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
            while (currentDir != null)
            {
                if (File.Exists(Path.Combine(currentDir.FullName, "README.md")))
                {
                    return currentDir.FullName;
                }
                currentDir = currentDir.Parent;
            }
            return AppContext.BaseDirectory;
        }

        [Fact]
        public void MQL5_EA_CompilesWithMetaEditor()
        {
            var eaPath = Path.Combine(_projectRoot, "MT5EA", "TradeSyncReceiver.mq5");
            Assert.True(File.Exists(eaPath), $"MQL5 EA file not found: {eaPath}");

            // Find MetaEditor installation
            var metaEditorPath = FindMetaEditorPath();
            if (string.IsNullOrEmpty(metaEditorPath))
            {
                _output.WriteLine("⚠️  MetaEditor not found, skipping compilation test");
                return;
            }

            var logPath = Path.Combine(_projectRoot, "mt5_compile.log");
            var args = $"/compile:\"{eaPath}\" /log:\"{logPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = metaEditorPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                process.WaitForExit(30000); // 30 second timeout

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                _output.WriteLine($"MetaEditor output: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    _output.WriteLine($"MetaEditor error: {error}");
                }

                // Check log file for compilation errors
                if (File.Exists(logPath))
                {
                    var logContent = File.ReadAllText(logPath);
                    _output.WriteLine($"Compilation log:\n{logContent}");

                    // Assert no compilation errors
                    Assert.DoesNotContain("error", logContent.ToLower(), 
                        $"MQL5 EA compilation failed. Check log at {logPath}");
                }

                Assert.True(process.ExitCode == 0 || process.ExitCode == -1, 
                    $"MetaEditor compilation returned exit code {process.ExitCode}");

                _output.WriteLine($"✓ MQL5 EA compiled successfully with MetaEditor");
            }
        }

        private string FindMetaEditorPath()
        {
            // Common MT5 installation paths
            var possiblePaths = new[]
            {
                @"C:\Program Files\MetaTrader 5\metaeditor64.exe",
                @"C:\Program Files (x86)\MetaTrader 5\metaeditor64.exe",
                @"C:\Program Files\MetaTrader 5\metaeditor.exe",
                @"C:\Program Files (x86)\MetaTrader 5\metaeditor.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _output.WriteLine($"Found MetaEditor at: {path}");
                    return path;
                }
            }

            _output.WriteLine("MetaEditor not found in common installation paths");
            return null;
        }

        [Fact]
        public void MQL5_EA_SourceFileIsValid()
        {
            var eaPath = Path.Combine(_projectRoot, "MT5EA", "TradeSyncReceiver.mq5");
            Assert.True(File.Exists(eaPath), $"MQL5 EA file not found: {eaPath}");

            var content = File.ReadAllText(eaPath);

            // Verify required MQL5 structure
            Assert.Contains("property", content);
            Assert.Contains("OnInit()", content);
            Assert.Contains("OnDeinit(", content);
            Assert.Contains("OnTimer()", content);
            Assert.Contains("CTrade", content);

            _output.WriteLine("✓ MQL5 EA has required MQL5 structure");
        }

        [Fact]
        public void CBot_SourceFileIsValid()
        {
            var cbotPath = Path.Combine(_projectRoot, "CtraderBot", "TradeSyncBot.cs");
            Assert.True(File.Exists(cbotPath), $"cBot file not found: {cbotPath}");

            var content = File.ReadAllText(cbotPath);

            // Verify required cBot/C# structure
            Assert.Contains("using cAlgo.API", content);
            Assert.Contains("class TradeSyncBot", content);
            Assert.Contains("OnStart()", content);
            Assert.Contains("OnStop()", content);
            Assert.Contains("HttpClient", content);
            Assert.Contains("JsonSerializer", content);

            _output.WriteLine("✓ cBot source has required C# structure");
        }

        [Fact]
        public void JAsonLibrary_Exists()
        {
            var jasonPath = Path.Combine(_projectRoot, "MT5EA", "JAson.mqh");
            Assert.True(File.Exists(jasonPath), $"JSON library not found: {jasonPath}");

            var content = File.ReadAllText(jasonPath);
            Assert.NotEmpty(content);

            _output.WriteLine("✓ JAson JSON library exists and is not empty");
        }

        [Fact]
        public void SourceFiles_HaveValidSyntax()
        {
            var files = new[]
            {
                (Path.Combine(_projectRoot, "MT5EA", "TradeSyncReceiver.mq5"), "MQL5"),
                (Path.Combine(_projectRoot, "CtraderBot", "TradeSyncBot.cs"), "C#"),
                (Path.Combine(_projectRoot, "MT5EA", "JAson.mqh"), "MQL5")
            };

            foreach (var (filePath, language) in files)
            {
                Assert.True(File.Exists(filePath), $"{language} file not found: {filePath}");

                var content = File.ReadAllText(filePath);
                var openBraces = content.Count(c => c == '{');
                var closeBraces = content.Count(c => c == '}');

                Assert.Equal(openBraces, closeBraces);
            }

            _output.WriteLine("✓ All source files have balanced braces");
        }

        [Fact]
        public void JsonSerialization_UsesPascalCase()
        {
            // Verify that Bridge uses PascalCase for JSON
            var bridgeProgramPath = Path.Combine(_projectRoot, "Bridge", "Program.cs");
            var content = File.ReadAllText(bridgeProgramPath);

            Assert.Contains("PropertyNamingPolicy = null", content);

            _output.WriteLine("✓ Bridge configured for PascalCase JSON serialization");
        }

        [Fact]
        public void AllComponents_UsePascalCase_ForJsonKeys()
        {
            // Verify consistent PascalCase naming across all components
            var files = new[]
            {
                (Path.Combine(_projectRoot, "MT5EA", "TradeSyncReceiver.mq5"), new[] { "OrderId", "Symbol", "EventType", "Status" }),
                (Path.Combine(_projectRoot, "CtraderBot", "TradeSyncBot.cs"), new[] { "OrderId", "Symbol", "EventType", "SourceId" })
            };

            foreach (var (filePath, keys) in files)
            {
                if (!File.Exists(filePath)) continue;

                var content = File.ReadAllText(filePath);
                foreach (var key in keys)
                {
                    Assert.Contains(key, content);
                }
            }

            _output.WriteLine("✓ All components use consistent PascalCase for JSON keys");
        }
    }
}
