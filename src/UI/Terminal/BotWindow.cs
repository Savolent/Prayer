using Terminal.Gui;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Channels;

public class BotWindow
{
    private enum StateTab
    {
        Space,
        Trade
    }

    private TextView _state;
    private ListView _objective;
    private ListView _memory;
    private ListView _actions;
    private TextView _input;
    private Button _changeObjectiveButton;
    private Button _saveExampleButton;
    private Button _executeButton;
    private CheckBox _loopCheckBox;
    private volatile bool _loopEnabled;

    private ChannelWriter<string>? _commandWriter;
    private ChannelWriter<string>? _controlInputWriter;
    private ChannelWriter<string>? _generateScriptWriter;
    private ChannelWriter<bool>? _saveExampleWriter;
    private ChannelWriter<bool>? _executeScriptWriter;
    private ChannelReader<string>? _statusReader;
    private ChannelWriter<string>? _switchBotWriter;
    private ChannelWriter<AddBotRequest>? _addBotWriter;

    private Window _win;
    private FrameView _playerFrame;
    private View _playerStack;
    private View _topInfoBar;
    private Button _spaceTabButton;
    private Button _tradeTabButton;
    private StateTab _selectedStateTab = StateTab.Space;
    private FrameView _objectiveFrame;
    private FrameView _memoryFrame;
    private FrameView _actionsFrame;
    private FrameView _inputFrame;
    private ColorScheme _scriptSideScheme;
    private ColorScheme _activeBotScheme;
    private string? _currentControlInput;
    private string _lastSpaceStateMarkdown = "";
    private string? _lastTradeStateMarkdown;
    private readonly List<View> _playerControls = new();
    private string _lastPlayerSignature = "";
    private volatile bool _uiReady;

    public bool LoopEnabled => _loopEnabled;

    // -----------------------------
    // Wiring
    // -----------------------------
    public void SetCommandWriter(ChannelWriter<string> writer)
    {
        _commandWriter = writer;
    }

    public void SetControlInputWriter(ChannelWriter<string> writer)
    {
        _controlInputWriter = writer;
    }

    public void SetGenerateScriptWriter(ChannelWriter<string> writer)
    {
        _generateScriptWriter = writer;
    }

    public void SetSaveExampleWriter(ChannelWriter<bool> writer)
    {
        _saveExampleWriter = writer;
    }

    public void SetExecuteScriptWriter(ChannelWriter<bool> writer)
    {
        _executeScriptWriter = writer;
    }

    public void SetStatusReader(ChannelReader<string> reader)
    {
        _statusReader = reader;
    }

    public void SetSwitchBotWriter(ChannelWriter<string> writer)
    {
        _switchBotWriter = writer;
    }

    public void SetAddBotWriter(ChannelWriter<AddBotRequest> writer)
    {
        _addBotWriter = writer;
    }

    // -----------------------------
    // UI Boot
    // -----------------------------
    public void Run()
    {
        Application.Init();

        // -----------------------------
        // Ctrl+C handling (CRITICAL)
        // -----------------------------
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            Application.RequestStop();
        };

        // -----------------------------
        // Color schemes
        // -----------------------------
        var baseScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.Green, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black)
        };

        var stateScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.Gray, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.Green, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black)
        };

        _scriptSideScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.White, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.BrightYellow, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.White, Color.Black)
        };

        _activeBotScheme = new ColorScheme
        {
            Normal = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black),
            Focus = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black),
            HotNormal = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black),
            HotFocus = Application.Driver.MakeAttribute(Color.BrightGreen, Color.Black)
        };

        // -----------------------------
        // Window
        // -----------------------------
        _win = new Window("Servator")
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ColorScheme = baseScheme
        };

        _playerFrame = new FrameView("Players")
        {
            X = 0,
            Y = 0,
            Width = 24,
            Height = Dim.Fill(),
            ColorScheme = baseScheme
        };
        _playerStack = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _playerFrame.Add(_playerStack);

        _topInfoBar = new View
        {
            X = Pos.Right(_playerFrame),
            Y = 0,
            Width = Dim.Fill(40),
            Height = 1,
            ColorScheme = baseScheme
        };
        _spaceTabButton = new Button("Space")
        {
            X = 0,
            Y = 0,
            Width = 9,
            Height = 1
        };
        _spaceTabButton.Clicked += () =>
        {
            _selectedStateTab = StateTab.Space;
            UpdateStateTabButtons(showTradeTab: _tradeTabButton.Visible);
            ApplySelectedStateTabText();
        };

        _tradeTabButton = new Button("Trade")
        {
            X = Pos.Right(_spaceTabButton) + 1,
            Y = 0,
            Width = 9,
            Height = 1,
            Visible = false
        };
        _tradeTabButton.Clicked += () =>
        {
            _selectedStateTab = StateTab.Trade;
            UpdateStateTabButtons(showTradeTab: true);
            ApplySelectedStateTabText();
        };

        _topInfoBar.Add(_spaceTabButton, _tradeTabButton);
        UpdateStateTabButtons(showTradeTab: false);

        RefreshPlayerStack(Array.Empty<BotTab>(), null);

        // -----------------------------
        // Layout (unchanged)
        // -----------------------------
        var stateFrame = new FrameView("State")
        {
            X = Pos.Right(_playerFrame),
            Y = 1,
            Width = Dim.Fill(40),
            Height = Dim.Fill(),
            ColorScheme = stateScheme
        };

        _state = new TextView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true
        };

        stateFrame.Add(_state);

        _objectiveFrame = new FrameView("Script")
        {
            X = Pos.Right(stateFrame),
            Y = 0,
            Width = 40,
            Height = Dim.Percent(16),
            ColorScheme = _scriptSideScheme
        };

        _objective = new ListView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };

        _loopCheckBox = new CheckBox("Loop")
        {
            X = 0,
            Y = Pos.AnchorEnd(2),
            Width = 12,
            Height = 1,
            Checked = false
        };
        _loopCheckBox.Toggled += _ => _loopEnabled = _loopCheckBox.Checked;

        _executeButton = new Button("Execute")
        {
            X = Pos.Right(_loopCheckBox) + 1,
            Y = Pos.AnchorEnd(2),
            Width = 11,
            Height = 1
        };
        _executeButton.Clicked += ExecuteScriptNow;

        _changeObjectiveButton = new Button("Edit Script")
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = 16,
            Height = 1
        };
        _changeObjectiveButton.Clicked += OpenObjectiveEditor;

        _saveExampleButton = new Button("Thumbs Up")
        {
            X = Pos.Right(_changeObjectiveButton) + 1,
            Y = Pos.AnchorEnd(1),
            Width = 12,
            Height = 1
        };
        _saveExampleButton.Clicked += SaveCurrentScriptAsExample;

        _objectiveFrame.Add(_objective);
        _objectiveFrame.Add(_loopCheckBox);
        _objectiveFrame.Add(_executeButton);
        _objectiveFrame.Add(_changeObjectiveButton);
        _objectiveFrame.Add(_saveExampleButton);

        _memoryFrame = new FrameView("Memory")
        {
            X = Pos.Right(stateFrame),
            Y = Pos.Bottom(_objectiveFrame),
            Width = 40,
            Height = Dim.Percent(30),
            ColorScheme = _scriptSideScheme
        };

        _memory = new ListView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _memoryFrame.Add(_memory);

        _actionsFrame = new FrameView("Available Actions")
        {
            X = Pos.Right(stateFrame),
            Y = Pos.Bottom(_memoryFrame),
            Width = 40,
            Height = Dim.Fill(3),
            ColorScheme = _scriptSideScheme
        };

        _actions = new ListView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };

        _actionsFrame.Add(_actions);

        _inputFrame = new FrameView("Prompt")
        {
            X = Pos.Right(stateFrame),
            Y = Pos.AnchorEnd(5),
            Width = 40,
            Height = 5,
            ColorScheme = _scriptSideScheme
        };

        _input = new TextView()
        {
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        _inputFrame.Add(_input);

        _input.KeyPress += args =>
        {
            if (args.KeyEvent.Key == Key.Enter ||
                args.KeyEvent.Key == (Key.CtrlMask | Key.S))
            {
                SubmitInput();
                args.Handled = true;
            }
        };

        _win.Add(_playerFrame, _topInfoBar, stateFrame, _memoryFrame, _objectiveFrame, _actionsFrame, _inputFrame);
        Application.Top.Add(_win);

        ApplyControlMode(ControlModeKind.ScriptMode);
        _uiReady = true;

        // -----------------------------
        // Exit keys
        // -----------------------------
        _win.KeyPress += args =>
        {
            if (args.KeyEvent.Key == Key.Esc ||
                args.KeyEvent.Key == (Key.CtrlMask | Key.C))
            {
                Application.RequestStop();
                args.Handled = true;
            }
        };

        // -----------------------------
        // STATUS CHANNEL
        // -----------------------------
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(100), _ =>
        {
            if (_statusReader != null)
            {
                while (_statusReader.TryRead(out var status))
                {
                    if (status.StartsWith("Switched to ", StringComparison.OrdinalIgnoreCase))
                        continue;
                    _win.Title = $"Servator - {status}";
                }
            }
            return true;
        });

        try
        {
            Application.Run();
        }
        finally
        {
            // CRITICAL: ensures driver + mouse cleanup
            Application.Shutdown();
        }
    }
    // -----------------------------
    // Render (thread-safe)
    // -----------------------------
    public void Render(
        string spaceStateMarkdown,
        string? tradeStateMarkdown,
        IReadOnlyList<string> memory,
        string? controlInput,
        int? currentScriptLine,
        ControlModeKind mode,
        IReadOnlyList<string> actions,
        string? lastGenerationPrompt,
        IReadOnlyList<BotTab> bots,
        string? activeBotId)
    {
        if (!_uiReady || Application.MainLoop == null)
            return;

        Application.MainLoop.Invoke(() =>
        {
            ApplyControlMode(mode);
            _currentControlInput = controlInput;
            RefreshPlayerStack(bots, activeBotId);
            bool showTradeTab = !string.IsNullOrWhiteSpace(tradeStateMarkdown);
            UpdateStateTabButtons(showTradeTab);
            _lastSpaceStateMarkdown = spaceStateMarkdown ?? "";
            _lastTradeStateMarkdown = tradeStateMarkdown;

            ApplySelectedStateTabText();

            var memoryDisplay = (memory ?? new List<string>())
                .Reverse()
                .ToList();
            _memory.SetSource((System.Collections.IList)memoryDisplay);
            if (string.IsNullOrWhiteSpace(controlInput))
            {
                _objective.SetSource(new List<string>
                {
                    "(no script loaded)"
                });
            }
            else
            {
                var objectiveLines = controlInput
                    .Replace("\r", "")
                    .Split('\n', StringSplitOptions.None)
                    .Select((line, idx) =>
                    {
                        var lineNo = idx + 1;
                        var marker = currentScriptLine.HasValue && currentScriptLine.Value == lineNo
                            ? ">"
                            : " ";
                        return $"{marker}{line}";
                    })
                    .ToList();

                _objective.SetSource(objectiveLines.Count > 0
                    ? objectiveLines
                    : new List<string> { controlInput.Trim() });
            }

            if (actions != null && actions.Count > 0)
            {
                _actions.SetSource((System.Collections.IList)actions);
            }
            else
            {
                _actions.SetSource(new List<string> { "(no actions available)" });
            }
        });
    }

    private void SubmitInput()
    {
        var text = _input.Text.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return;

        _generateScriptWriter?.TryWrite(text);
        _input.Text = "";
        _input.SetFocus();
    }

    private void OpenObjectiveEditor()
    {
        string tempFile = Path.Combine(
            Path.GetTempPath(),
            $"spacemolt-script-{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(tempFile, _currentControlInput ?? "");

            string editor = Environment.GetEnvironmentVariable("VISUAL")
                ?? Environment.GetEnvironmentVariable("EDITOR")
                ?? "nano";
            string command = $"{editor} '{tempFile.Replace("'", "'\"'\"'")}'";

            var startInfo = new ProcessStartInfo("/bin/bash")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-lc");
            startInfo.ArgumentList.Add(command);

            using var process = Process.Start(startInfo);
            process?.WaitForExit();

            if (process?.ExitCode == 0)
            {
                var edited = File.ReadAllText(tempFile);
                if (!string.IsNullOrWhiteSpace(edited))
                    _controlInputWriter?.TryWrite(edited);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // Temp file cleanup failures should not break UI.
            }

            Application.Refresh();
            _input.SetFocus();
        }
    }

    private void OpenAddBotModal()
    {
        var ok = new Button("Add");
        var cancel = new Button("Cancel");
        var dialog = new Dialog("Add Bot", 72, 17, ok, cancel);

        var modeLabel = new Label("Mode:")
        {
            X = 1,
            Y = 1,
            Width = 13
        };
        var modeRadio = new RadioGroup(
            new NStack.ustring[] { "_Login", "_Register" },
            0)
        {
            X = Pos.Right(modeLabel),
            Y = Pos.Top(modeLabel)
        };

        var userLabel = new Label("Username:")
        {
            X = 1,
            Y = Pos.Bottom(modeRadio) + 1,
            Width = 13
        };
        var userInput = new TextField("")
        {
            X = Pos.Right(userLabel),
            Y = Pos.Top(userLabel),
            Width = Dim.Fill(2)
        };

        var passwordLabel = new Label("Password:")
        {
            X = 1,
            Y = Pos.Bottom(userLabel) + 1,
            Width = 13
        };
        var passwordInput = new TextField("")
        {
            X = Pos.Right(passwordLabel),
            Y = Pos.Top(passwordLabel),
            Width = Dim.Fill(2),
            Secret = true
        };

        var registrationCodeLabel = new Label("Reg Code:")
        {
            X = 1,
            Y = Pos.Top(passwordLabel),
            Width = 13,
            Visible = false
        };
        var registrationCodeInput = new TextField("")
        {
            X = Pos.Right(registrationCodeLabel),
            Y = Pos.Top(registrationCodeLabel),
            Width = Dim.Fill(2),
            Visible = false
        };

        var empireLabel = new Label("Empire:")
        {
            X = 1,
            Y = Pos.Bottom(registrationCodeLabel) + 1,
            Width = 13,
            Visible = false
        };
        var empires = new List<string> { "solarian", "voidborn", "crimson", "nebula", "outerrim" };
        var empireDropdown = new ComboBox(empires)
        {
            X = Pos.Right(empireLabel),
            Y = Pos.Top(empireLabel),
            Width = Dim.Fill(2),
            Height = 1,
            ReadOnly = true,
            Text = empires[0],
            Visible = false
        };

        void ApplyMode()
        {
            bool isRegister = modeRadio.SelectedItem == 1;
            passwordLabel.Visible = !isRegister;
            passwordInput.Visible = !isRegister;
            registrationCodeLabel.Visible = isRegister;
            registrationCodeInput.Visible = isRegister;
            empireLabel.Visible = isRegister;
            empireDropdown.Visible = isRegister;
            dialog.SetNeedsDisplay();
        }

        modeRadio.SelectedItemChanged += _ => ApplyMode();
        ApplyMode();

        dialog.Add(
            modeLabel,
            modeRadio,
            userLabel,
            userInput,
            passwordLabel,
            passwordInput,
            registrationCodeLabel,
            registrationCodeInput,
            empireLabel,
            empireDropdown);

        bool submitted = false;
        ok.Clicked += () =>
        {
            bool isRegister = modeRadio.SelectedItem == 1;
            var username = userInput.Text.ToString() ?? "";
            var password = passwordInput.Text.ToString() ?? "";
            var registrationCode = registrationCodeInput.Text.ToString() ?? "";
            string empire = empires[0];
            if (empireDropdown.SelectedItem >= 0 && empireDropdown.SelectedItem < empires.Count)
                empire = empires[empireDropdown.SelectedItem];
            else if (!string.IsNullOrWhiteSpace(empireDropdown.Text?.ToString()))
                empire = empireDropdown.Text.ToString()!;

            if (!string.IsNullOrWhiteSpace(username))
            {
                if (!isRegister && !string.IsNullOrWhiteSpace(password))
                {
                    _addBotWriter?.TryWrite(new AddBotRequest(
                        AddBotMode.Login,
                        username,
                        Password: password));
                    submitted = true;
                }
                else if (isRegister &&
                         !string.IsNullOrWhiteSpace(registrationCode) &&
                         !string.IsNullOrWhiteSpace(empire))
                {
                    _addBotWriter?.TryWrite(new AddBotRequest(
                        AddBotMode.Register,
                        username,
                        RegistrationCode: registrationCode,
                        Empire: empire));
                    submitted = true;
                }
            }

            Application.RequestStop();
        };

        cancel.Clicked += () => Application.RequestStop();
        Application.Run(dialog);

        if (submitted)
            _input.SetFocus();
    }

    private void SaveCurrentScriptAsExample()
    {
        _saveExampleWriter?.TryWrite(true);
        _input.SetFocus();
    }

    private void ExecuteScriptNow()
    {
        _executeScriptWriter?.TryWrite(true);
        _input.SetFocus();
    }

    private void ApplyControlMode(ControlModeKind mode)
    {
        _objectiveFrame.ColorScheme = _scriptSideScheme;
        _memoryFrame.ColorScheme = _scriptSideScheme;
        _actionsFrame.ColorScheme = _scriptSideScheme;
        _inputFrame.ColorScheme = _scriptSideScheme;
        _objectiveFrame.Height = Dim.Percent(44);
        _memoryFrame.Height = Dim.Percent(26);
        _objectiveFrame.Title = "Script";
        _changeObjectiveButton.Text = "Edit Script";
        _executeButton.Visible = true;
        _saveExampleButton.Visible = true;
    }

    private static int CountRows(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        int rows = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                rows++;
        }

        return rows;
    }

    private void ApplySelectedStateTabText()
    {
        bool showTradeTab = !string.IsNullOrWhiteSpace(_lastTradeStateMarkdown);
        var stateMarkdown = _selectedStateTab == StateTab.Trade && showTradeTab
            ? _lastTradeStateMarkdown!
            : _lastSpaceStateMarkdown;

        int previousTopRow = _state.TopRow;
        int previousLeftColumn = _state.LeftColumn;

        _state.Text = string.IsNullOrWhiteSpace(stateMarkdown)
            ? "(no state)"
            : stateMarkdown;

        int visibleRows = Math.Max(1, _state.Bounds.Height - 1);
        int totalRows = CountRows(_state.Text?.ToString() ?? "");
        int maxTopRow = Math.Max(0, totalRows - visibleRows);
        _state.TopRow = Math.Min(previousTopRow, maxTopRow);
        _state.LeftColumn = Math.Max(0, previousLeftColumn);
    }

    private void UpdateStateTabButtons(bool showTradeTab)
    {
        _tradeTabButton.Visible = showTradeTab;
        if (!showTradeTab && _selectedStateTab == StateTab.Trade)
            _selectedStateTab = StateTab.Space;

        _spaceTabButton.ColorScheme = _selectedStateTab == StateTab.Space
            ? _activeBotScheme
            : _topInfoBar.ColorScheme;
        _tradeTabButton.ColorScheme = _selectedStateTab == StateTab.Trade
            ? _activeBotScheme
            : _topInfoBar.ColorScheme;
    }

    private void RefreshPlayerStack(IReadOnlyList<BotTab> bots, string? activeBotId)
    {
        var labels = string.Join(",", bots.Select(b => b.Label));
        var signature = $"{bots.Count}|{activeBotId}|{labels}";
        if (signature == _lastPlayerSignature)
            return;

        if (signature != _lastPlayerSignature)
        {
            _lastPlayerSignature = signature;
            try
            {
                File.AppendAllText(
                    AppPaths.AuthFlowLogFile,
                    $"[{DateTime.UtcNow:O}] ui_refresh_player_stack | tabs_changed | count={bots.Count} | active={activeBotId ?? "(null)"} | labels=[{labels}]{Environment.NewLine}");
            }
            catch
            {
                // Debug logging must not break UI.
            }
        }

        foreach (var view in _playerControls)
            _playerStack.Remove(view);
        _playerControls.Clear();

        int y = 0;
        foreach (var bot in bots)
        {
            string label = bot.Label;
            string botId = bot.Id;

            var button = new Button(label)
            {
                X = 0,
                Y = y,
                Width = Dim.Fill(),
                Height = 1
            };
            if (bot.Id == activeBotId)
                button.ColorScheme = _activeBotScheme;
            button.Clicked += () => _switchBotWriter?.TryWrite(botId);

            _playerControls.Add(button);
            _playerStack.Add(button);
            y += 1;
        }

        if (bots.Count == 0)
        {
            var emptyLabel = new Label("(no bots loaded)")
            {
                X = 0,
                Y = 0
            };
            _playerControls.Add(emptyLabel);
            _playerStack.Add(emptyLabel);
            y = 1;
        }

        var addButton = new Button("+")
        {
            X = 0,
            Y = y + 1,
            Width = Dim.Fill(),
            Height = 1
        };
        addButton.Clicked += OpenAddBotModal;

        _playerControls.Add(addButton);
        _playerStack.Add(addButton);
    }
}
