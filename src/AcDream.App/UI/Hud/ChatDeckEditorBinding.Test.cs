using AcDream.Core.Chat.Deck;

namespace AcDream.App.UI.Hud;

public sealed partial class ChatDeckEditorBinding
{
    private ChatDeckChannel _testChannel = ChatDeckChannel.Say;
    private string _testSender = "";
    private string _testText = "";
    private ChatDeckLineFacts? _testFacts;   // a real line from the transcript, until a field is edited
    private object? _testFor;
    private IReadOnlyList<ResultRow> _testResult = [];

    private void ResetTest()
    {
        _testChannel = ChatDeckChannel.Say;
        _testSender = _testText = "";
        _testFacts = null;
        _testFor = null;
    }

    private void StartTest(ChatDeckLineFacts line)
    {
        _ = M;                     // bind the model first: its first access resets the panes
        _page = ChatDeckEditorPage.Rules;
        _pane = Pane.Test;
        _testFacts = line;
        _testChannel = line.Channel;
        _testSender = line.Sender;
        _testText = line.Text;
        _testFor = null;
    }

    public bool TestOpen
    {
        get
        {
            _ = M;                 // a model swap resets the panes; read it before trusting the pane
            return _pane == Pane.Test;
        }
    }

    public Action ShowTest => () => { _ = M; _pane = Pane.Test; _page = ChatDeckEditorPage.Rules; };
    public bool TestFromTranscript { get { _ = M; return _testFacts is not null; } }

    public IReadOnlyList<string> ChannelNames { get; } =
        Enum.GetValues<ChatDeckChannel>().Select(ChatDeckChannels.DisplayName).ToList();
    public string TestChannel { get { _ = M; return ChatDeckChannels.DisplayName(_testChannel); } }
    public Action<string> SetTestChannel => v =>
    {
        _ = M;
        foreach (ChatDeckChannel c in Enum.GetValues<ChatDeckChannel>())
            if (ChatDeckChannels.DisplayName(c) == v) { _testChannel = c; _testFacts = null; }
    };
    public string TestSender { get { _ = M; return _testSender; } }
    public Action<string> SetTestSender => v => { _ = M; _testSender = v; _testFacts = null; };
    public string TestText { get { _ = M; return _testText; } }
    public Action<string> SetTestText => v => { _ = M; _testText = v; _testFacts = null; };

    public sealed record ResultRow(string Text, uint Colour);

    public IReadOnlyList<ResultRow> TestResult
    {
        get
        {
            if (M is not { } m || (_testFacts is null && _testText.Length == 0)) return [];
            var key = (_testFacts, _testChannel, _testSender, _testText, m.Settings, m.CompiledRules);
            if (Equals(key, _testFor)) return _testResult;
            _testFor = key;
            ChatDeckLineFacts facts = _testFacts ?? ChatDeckTestLine.Facts(_testChannel, _testSender, _testText, m.CharacterName);
            ChatDeckVerdict verdict = m.Test(facts);
            uint ink = 0xFF000000u | InkColour, red = 0xFF000000u | ProblemColour;
            _testResult = ChatDeckVerdictText.Lines(verdict, m.Settings)
                .Select((text, i) => new ResultRow(text, i == 0 && verdict.Hidden ? red : ink)).ToList();
            return _testResult;
        }
    }

    private const string TestMarkup = """
            <group layout="column" gap="4" h="270" visible="{TestOpen}">
              <separator h="1" />
              <group layout="row" gap="8" align="center" h="22">
                <label text="Test a line against the rules as they are now" grow="1" />
                <button text="Done" onclick="{ClosePane}" w="60" h="22" />
              </group>
              <group layout="row" gap="8" align="center" h="22">
                <label text="Channel" w="60" />
                <menu items="{ChannelNames}" selected="{TestChannel}" onchange="{SetTestChannel}" rows="16" w="160" h="20" />
                <label text="Sender" w="50" />
                <field text="{TestSender}" onchange="{SetTestSender}" maxlength="40" grow="1" h="20" />
              </group>
              <field text="{TestText}" onchange="{SetTestText}" maxlength="2000" h="20" />
              <label text="(the line from the transcript)" visible="{TestFromTranscript}" h="16" />
              <group layout="column" gap="1" scroll="y" grow="1">
                <repeat items="{TestResult}">
                  <label text="{Text}" color="{Colour}" h="16" />
                </repeat>
              </group>
            </group>
        """;
}
