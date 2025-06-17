using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoKiwi;

namespace AutoKiwi
{
    // SUPER jednoduchý HTTP server - BEZ WebSockets, BEZ extra závislostí
    public class SimpleAutoKiwiAPI
    {
        private HttpListener _listener;
        private bool _isRunning = false;
        public string CurrentPosition { get; private set; } = "Architect";
        public string LastStatus { get; private set; } = "Ready";

        public async Task StartAsync()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:8080/");
                _listener.Start();
                _isRunning = true;

                Console.WriteLine("🎬 AutoKiwi API running on http://localhost:8080");

                while (_isRunning)
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Server error: {ex.Message}");
            }
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            try
            {
                // CORS headers
                context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
                context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                context.Response.Headers.Add("Access-Control-Allow-Headers", "*");

                if (context.Request.HttpMethod == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                    return;
                }

                var path = context.Request.Url.AbsolutePath;
                var method = context.Request.HttpMethod;

                string response = "";

                switch (path)
                {
                    case "/status":
                        response = GetStatus();
                        break;
                    case "/switch-position":
                        if (method == "POST")
                        {
                            response = await HandlePositionSwitch(context.Request);
                        }
                        break;
                    case "/generate-code":
                        if (method == "POST")
                        {
                            response = await HandleCodeGeneration(context.Request);
                        }
                        break;
                    case "/chat":
                        if (method == "POST")
                        {
                            response = await HandleChat(context.Request);
                        }
                        break;
                    default:
                        response = "AutoKiwi API - OK";
                        break;
                }

                var buffer = Encoding.UTF8.GetBytes(response);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                context.Response.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Request error: {ex.Message}");
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
        }

        private string GetStatus()
        {
            // Escapuj všechny stringy pro JSON
            var position = EscapeJsonString(CurrentPosition);
            var status = EscapeJsonString(LastStatus);

            return $@"{{
        ""server"": ""AutoKiwi API"",
        ""status"": ""running"",
        ""claudePosition"": ""{position}"",
        ""lastStatus"": ""{status}"",
        ""timestamp"": ""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}"",
        ""memoryUsage"": {GC.GetTotalMemory(false)}
    }}";
        }

        private async Task<string> HandlePositionSwitch(HttpListenerRequest request)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    var body = await reader.ReadToEndAsync();

                    // Safer JSON parsing
                    if (body.Contains("\"position\""))
                    {
                        var start = body.IndexOf("\"position\":\"") + 12;
                        var end = body.IndexOf("\"", start);
                        if (end > start)
                        {
                            var position = body.Substring(start, end - start);
                            position = UnescapeJsonString(position); // Clean up

                            CurrentPosition = position;
                            LastStatus = $"Switched to {position}";

                            Console.WriteLine($"🎯 Claude switched to: {position}");

                            var escapedPosition = EscapeJsonString(position);
                            return $@"{{
                        ""success"": true,
                        ""position"": ""{escapedPosition}"",
                        ""message"": ""Position switched successfully""
                    }}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Position switch error: {ex.Message}");
            }

            return @"{""success"": false, ""message"": ""Failed to switch position""}";
        }

        private async Task<string> HandleChat(HttpListenerRequest request)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    var body = await reader.ReadToEndAsync();
                    Console.WriteLine($"📥 Chat request body: {body}");

                    // Extract message
                    var msgStart = body.IndexOf("\"message\":\"") + 11;
                    var msgEnd = body.IndexOf("\"", msgStart);
                    var message = msgStart > 10 && msgEnd > msgStart ?
                        body.Substring(msgStart, msgEnd - msgStart) : "empty message";

                    message = UnescapeJsonString(message);
                    Console.WriteLine($"📝 Extracted message: {message}");

                    // Generate response based on message content
                    string response;
                    if (message.ToLower().Contains("position"))
                    {
                        response = $"My current position is {CurrentPosition}. I'm ready to help with {CurrentPosition.ToLower()} tasks!";
                    }
                    else if (message.ToLower().Contains("hello"))
                    {
                        response = $"Hello! I'm Claude in {CurrentPosition} mode. How can I assist you today?";
                    }
                    else if (message.ToLower().Contains("switch") && message.ToLower().Contains("to"))
                    {
                        // Extract target position
                        var positions = new[] { "architect", "generator", "debugger", "tester", "designer", "optimizer" };
                        var targetPosition = positions.FirstOrDefault(p => message.ToLower().Contains(p));
                        if (targetPosition != null)
                        {
                            var newPosition = char.ToUpper(targetPosition[0]) + targetPosition.Substring(1);
                            CurrentPosition = newPosition;
                            response = $"Switched to {newPosition} mode! Ready for {newPosition.ToLower()} tasks.";
                        }
                        else
                        {
                            response = "I can switch to: Architect, Generator, Debugger, Tester, Designer, or Optimizer. Which would you like?";
                        }
                    }
                    else if (message.ToLower().Contains("help"))
                    {
                        response = $"I'm Claude in {CurrentPosition} mode. I can help with code generation, debugging, testing, and more. What do you need?";
                    }
                    else
                    {
                        response = $"[{CurrentPosition}] I understand you said: '{message}'. As a {CurrentPosition.ToLower()}, I'm here to help with your development needs!";
                    }

                    Console.WriteLine($"💬 Chat response: {response}");
                    LastStatus = $"Chat: {message.Substring(0, Math.Min(message.Length, 20))}...";

                    // Safe JSON response
                    var escapedResponse = EscapeJsonString(response);
                    var escapedMessage = EscapeJsonString(message);
                    var escapedPosition = EscapeJsonString(CurrentPosition);

                    var jsonResponse = $@"{{
    ""success"": true,
    ""response"": ""{escapedResponse}"",
    ""position"": ""{escapedPosition}"",
    ""originalMessage"": ""{escapedMessage}"",
    ""timestamp"": ""{DateTime.Now:HH:mm:ss}""
}}";

                    Console.WriteLine($"📤 JSON response: {jsonResponse}");
                    return jsonResponse;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Chat error: {ex.Message}");
                var errorResponse = $@"{{
    ""success"": false,
    ""message"": ""Chat error: {EscapeJsonString(ex.Message)}"",
    ""position"": ""{EscapeJsonString(CurrentPosition)}""
}}";
                return errorResponse;
            }
        }
        private async Task<string> HandleCodeGeneration(HttpListenerRequest request)
        {
            try
            {
                using (var reader = new StreamReader(request.InputStream))
                {
                    var body = await reader.ReadToEndAsync();
                    Console.WriteLine($"📥 Code generation request: {body}");

                    var promptStart = body.IndexOf("\"prompt\":\"") + 10;
                    var promptEnd = body.IndexOf("\"", promptStart);
                    var prompt = promptStart > 9 && promptEnd > promptStart ?
                        body.Substring(promptStart, promptEnd - promptStart) : "simple application";

                    prompt = UnescapeJsonString(prompt);
                    Console.WriteLine($"⚡ Generating code for: {prompt}");

                    // Generate code based on current position and prompt
                    string generatedCode;
                    switch (CurrentPosition.ToLower())
                    {
                        case "architect":
                            generatedCode = GenerateArchitectCode(prompt);
                            break;
                        case "debugger":
                            generatedCode = GenerateDebuggerCode(prompt);
                            break;
                        case "tester":
                            generatedCode = GenerateTesterCode(prompt);
                            break;
                        default:
                            generatedCode = GenerateDefaultCode(prompt);
                            break;
                    }

                    LastStatus = $"Generated: {prompt}";

                    var escapedCode = EscapeJsonString(generatedCode);
                    var escapedPrompt = EscapeJsonString(prompt);

                    return $@"{{
    ""success"": true,
    ""code"": ""{escapedCode}"",
    ""prompt"": ""{escapedPrompt}"",
    ""position"": ""{EscapeJsonString(CurrentPosition)}"",
    ""timestamp"": ""{DateTime.Now:HH:mm:ss}""
}}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Code generation error: {ex.Message}");
                return $@"{{
    ""success"": false,
    ""message"": ""Code generation failed: {EscapeJsonString(ex.Message)}""
}}";
            }
        }

        private string GenerateArchitectCode(string prompt)
        {
            return $@"// 🏗️ ARCHITECTURAL DESIGN for: {prompt}
using System;
using System.Collections.Generic;

namespace AutoKiwi.Generated
{{
    // High-level architecture for {prompt}
    public interface I{prompt.Replace(" ", "")}Service
    {{
        Task<Result> ProcessAsync();
    }}

    public class {prompt.Replace(" ", "")}Architecture
    {{
        private readonly I{prompt.Replace(" ", "")}Service _service;
        
        public {prompt.Replace(" ", "")}Architecture(I{prompt.Replace(" ", "")}Service service)
        {{
            _service = service;
        }}

        public async Task<ArchitecturalResult> DesignSystemAsync()
        {{
            // Architectural design for {prompt}
            return new ArchitecturalResult
            {{
                Design = ""Modular architecture for {prompt}"",
                Components = new[] {{ ""Service Layer"", ""Data Layer"", ""UI Layer"" }}
            }};
        }}
    }}
}}";
        }

        private string GenerateDebuggerCode(string prompt)
        {
            return $@"// 🔍 DEBUGGING UTILITIES for: {prompt}
using System;
using System.Diagnostics;

namespace AutoKiwi.Debugging
{{
    public class {prompt.Replace(" ", "")}Debugger
    {{
        private static readonly TraceSource _trace = new TraceSource(""{prompt}"");

        public static void Debug{prompt.Replace(" ", "")}()
        {{
            try
            {{
                _trace.TraceInformation(""Starting debug session for {prompt}"");
                
                // Debug implementation for {prompt}
                Console.WriteLine(""🔍 Debugging: {prompt}"");
                
                // Breakpoint for analysis
                if (Debugger.IsAttached)
                {{
                    Debugger.Break();
                }}
                
                _trace.TraceInformation(""Debug session completed for {prompt}"");
            }}
            catch (Exception ex)
            {{
                _trace.TraceError($""Debug error for {prompt}: {{ex.Message}}"");
                throw;
            }}
        }}
    }}
}}";
        }

        private string GenerateTesterCode(string prompt)
        {
            return $@"// 🧪 UNIT TESTS for: {prompt}
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AutoKiwi.Tests
{{
    [TestClass]
    public class {prompt.Replace(" ", "")}Tests
    {{
        [TestMethod]
        public void Test{prompt.Replace(" ", "")}_ShouldWork()
        {{
            // Arrange
            var target = new {prompt.Replace(" ", "")}();
            
            // Act
            var result = target.Process();
            
            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.IsSuccess);
        }}

        [TestMethod]
        public void Test{prompt.Replace(" ", "")}_ShouldHandleErrors()
        {{
            // Test error handling for {prompt}
            Assert.ThrowsException<ArgumentNullException>(() => 
            {{
                var target = new {prompt.Replace(" ", "")}();
                target.ProcessWithNull(null);
            }});
        }}
    }}
}}";
        }

        private string GenerateDefaultCode(string prompt)
        {
            return $@"// ⚡ GENERATED CODE for: {prompt}
using System;

namespace AutoKiwi.Generated
{{
    public class {prompt.Replace(" ", "")}Handler
    {{
        public void Process{prompt.Replace(" ", "")}()
        {{
            Console.WriteLine(""Processing: {prompt}"");
            
            // Implementation for {prompt}
            var result = $""Generated result for: {prompt}"";
            
            Console.WriteLine($""Result: {{result}}"");
        }}
    }}
}}";
        }


        public void Stop()
        {
            _isRunning = false;
            _listener?.Stop();
            Console.WriteLine("🛑 AutoKiwi API stopped");
        }

        // Public metody pro volání z AutoKiwi UI
        public void SwitchPositionFromUI(string position)
        {
            CurrentPosition = position;
            LastStatus = $"UI switched to {position}";
            Console.WriteLine($"🎯 UI: Claude switched to {position}");
        }

        public void UpdateStatus(string status)
        {
            LastStatus = status;
            Console.WriteLine($"📊 Status: {status}");
        }

        private string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            return input
                .Replace("\\", "\\\\")  // Backslash
                .Replace("\"", "\\\"")  // Quote
                .Replace("\n", "\\n")   // Newline
                .Replace("\r", "\\r")   // Carriage return
                .Replace("\t", "\\t")   // Tab
                .Replace("\b", "\\b")   // Backspace
                .Replace("\f", "\\f");  // Form feed
        }

        private string UnescapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            return input
                .Replace("\\\\", "\\")
                .Replace("\\\"", "\"")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\b", "\b")
                .Replace("\\f", "\f");
        }
    }

    // Jednoduchá integrace do MainForm
    public partial class MainForm : Form
    {
        private SimpleAutoKiwiAPI _api;

        // Přidej do MainForm_Load nebo constructor
        private async void InitializeAPI()
        {
            _api = new SimpleAutoKiwiAPI();

            // Spusť API server v background
            _ = Task.Run(async () => await _api.StartAsync());

            // Update UI
            UpdateStatusLabel("🎬 AutoKiwi API: Running");
        }

        // Pomocná metoda pro update statusu
        private void UpdateStatusLabel(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action(() => UpdateStatusLabel(text)));
                return;
            }

            // Najdi status label v UI a aktualizuj
            foreach (Control control in Controls)
            {
                if (control is Label && control.Name.ToLower().Contains("status"))
                {
                    control.Text = text;
                    control.ForeColor = System.Drawing.Color.Green;
                    break;
                }
            }
        }

        // Testovací metoda - můžeš přidat button pro test
        private void TestAPIButton_Click(object sender, EventArgs e)
        {
            _api?.SwitchPositionFromUI("Debugger");
            _api?.UpdateStatus("Test completed");
            MessageBox.Show("API test completed!");
        }

        // Cleanup při zavření
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _api?.Stop();
            base.OnFormClosed(e);
        }
    }
}