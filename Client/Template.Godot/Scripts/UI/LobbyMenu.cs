using Godot;
using System;
using Template.Godot.Core;

namespace Template.Godot.UI;

public partial class LobbyMenu : CanvasLayer
{
    private Control _mainMenu;
    private Control _createPanel;
    private Control _joinPanel;
    private Control _waitingPanel;

    private Button _createBtn;
    private Button _joinBtn;
    private Button _offlineBtn;
    private CheckButton _useSaveCheck;

    private Label _lobbyCodeLabel;
    private Button _copyCodeBtn;
    private Button _startMatchBtn;
    private Button _backFromCreateBtn;

    private LineEdit _codeInput;
    private Button _confirmJoinBtn;
    private Button _backFromJoinBtn;

    private Label _statusLabel;
    private Button _backFromWaitBtn;

    public override void _Ready()
    {
        _mainMenu = GetNode<Control>("MainMenu");
        _createPanel = GetNode<Control>("CreatePanel");
        _joinPanel = GetNode<Control>("JoinPanel");
        _waitingPanel = GetNode<Control>("WaitingPanel");

        _createBtn = _mainMenu.GetNode<Button>("VBox/CreateBtn");
        _joinBtn = _mainMenu.GetNode<Button>("VBox/JoinBtn");
        _offlineBtn = _mainMenu.GetNode<Button>("VBox/OfflineBtn");
        _useSaveCheck = _mainMenu.GetNode<CheckButton>("VBox/UseSaveCheck");

        _lobbyCodeLabel = _createPanel.GetNode<Label>("VBox/CodeLabel");
        _copyCodeBtn = _createPanel.GetNode<Button>("VBox/CopyBtn");
        _startMatchBtn = _createPanel.GetNode<Button>("VBox/StartBtn");
        _backFromCreateBtn = _createPanel.GetNode<Button>("VBox/BackBtn");

        _codeInput = _joinPanel.GetNode<LineEdit>("VBox/CodeInput");
        _confirmJoinBtn = _joinPanel.GetNode<Button>("VBox/ConfirmBtn");
        _backFromJoinBtn = _joinPanel.GetNode<Button>("VBox/BackBtn");

        _statusLabel = _waitingPanel.GetNode<Label>("VBox/StatusLabel");
        _backFromWaitBtn = _waitingPanel.GetNode<Button>("VBox/BackBtn");

        _createBtn.Pressed += OnCreatePressed;
        _joinBtn.Pressed += OnJoinPressed;
        _offlineBtn.Pressed += OnOfflinePressed;
        _useSaveCheck.Visible = GameManager.Instance.HasSaveFile();

        _copyCodeBtn.Pressed += OnCopyCodePressed;
        _startMatchBtn.Pressed += OnStartMatchPressed;
        _backFromCreateBtn.Pressed += ShowMainMenu;

        _confirmJoinBtn.Pressed += OnConfirmJoinPressed;
        _backFromJoinBtn.Pressed += ShowMainMenu;

        _backFromWaitBtn.Pressed += ShowMainMenu;

        var gm = GameManager.Instance;
        gm.OnStatusChanged += (status) => CallDeferred(nameof(UpdateStatus), status);
        gm.OnLobbyCreated += (id) => CallDeferred(nameof(OnLobbyReady), id.ToString());
        gm.OnGameStarted += () => CallDeferred(nameof(OnGameReady));
        gm.OnError += (err) => CallDeferred(nameof(OnErrorReceived), err);

        ShowMainMenu();
    }

    private void ShowMainMenu()
    {
        _mainMenu.Visible = true;
        _createPanel.Visible = false;
        _joinPanel.Visible = false;
        _waitingPanel.Visible = false;
    }

    private void ShowPanel(Control panel)
    {
        _mainMenu.Visible = false;
        _createPanel.Visible = false;
        _joinPanel.Visible = false;
        _waitingPanel.Visible = false;
        panel.Visible = true;
    }

    private void OnCreatePressed()
    {
        ShowPanel(_waitingPanel);
        _statusLabel.Text = "Creating lobby...";
        _backFromWaitBtn.Visible = true;
        _ = GameManager.Instance.CreateLobby("Game Lobby");
    }

    private void OnLobbyReady(string lobbyIdStr)
    {
        ShowPanel(_createPanel);
        var code = lobbyIdStr;
        _lobbyCodeLabel.Text = code;
        _startMatchBtn.Disabled = false;
    }

    private void OnJoinPressed()
    {
        ShowPanel(_joinPanel);
        _codeInput.Text = "";
        _codeInput.GrabFocus();
    }

    private void OnConfirmJoinPressed()
    {
        var text = _codeInput.Text.Trim();
        if (!Guid.TryParse(text, out var lobbyId))
        {
            _statusLabel.Text = "Invalid lobby code.";
            ShowPanel(_waitingPanel);
            _backFromWaitBtn.Visible = true;
            return;
        }

        ShowPanel(_waitingPanel);
        _statusLabel.Text = "Joining lobby...";
        _backFromWaitBtn.Visible = true;
        _ = GameManager.Instance.JoinLobby(lobbyId);
    }

    private void OnOfflinePressed()
    {
        if (_useSaveCheck.ButtonPressed)
            GameManager.Instance.StartOfflineFromSave();
        else
            GameManager.Instance.StartOffline();
    }

    private void OnStartMatchPressed()
    {
        _startMatchBtn.Disabled = true;
        ShowPanel(_waitingPanel);
        _statusLabel.Text = "Starting match...";
        _backFromWaitBtn.Visible = false;

        if (_useSaveCheck.ButtonPressed)
        {
            var saveData = GameManager.Instance.LoadGameFromDisk();
            if (saveData != null)
                GameManager.Instance.SetPendingLoadState(saveData);
        }

        _ = GameManager.Instance.StartLobby();
    }

    private void OnCopyCodePressed()
    {
        DisplayServer.ClipboardSet(_lobbyCodeLabel.Text);
        _copyCodeBtn.Text = "Copied!";
        GetTree().CreateTimer(1.5).Timeout += () => _copyCodeBtn.Text = "Copy Code";
    }

    private void UpdateStatus(string status)
    {
        _statusLabel.Text = status;
    }

    private void OnGameReady()
    {
        Visible = false;
    }

    private void OnErrorReceived(string error)
    {
        _statusLabel.Text = $"Error: {error}";
        ShowPanel(_waitingPanel);
        _backFromWaitBtn.Visible = true;
    }
}
