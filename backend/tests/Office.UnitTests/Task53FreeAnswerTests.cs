using Office.Application.Texts;
using Office.Application.Understanding;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.53 (phases 2 and 3): with understanding v2 on, words the AI read that no approved text answers get the
/// writer's answer from the office facts and «دليل التعامل», checked by the code; the writer off or doubtful sends the
/// approved words; «دليل التعامل» reaches the writer only with the switch on, and is never sent.
/// </summary>
public sealed partial class BookingFlowTests
{
    private const string Playbook = "- السرية: كل كلام العميل مع المكتب سري.";

    private void FreeAnswerOn()
    {
        UnderstandingOn();
        ConversationalOn();
        _texts[TextKeys.FreeAnswer] = new ApprovedText(TextKeys.FreeAnswer, 1, "معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏", 300, true);
        _texts[TextKeys.OfficePlaybook] = new ApprovedText(TextKeys.OfficePlaybook, 1, Playbook, 600, true);
        _script["انا خايف مراتي تعرف"] = new("other", null, "unknown", null, 0.8);
    }

    [Fact]
    public async Task A_new_kind_of_moment_gets_the_writer_s_answer_from_the_facts_and_the_playbook()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم 🙏 كل كلام حضرتك مع المكتب سري، والمستشار هو اللي هيسمعك ويقولك تعمل إيه.";
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal([TextKeys.FreeAnswer], Keys(d));
        Assert.Equal(_writer.Reply, d.Actions[^1].Text);
        Assert.Equal(WrittenReply.Passed, d.Effects!.Written!.Guard);
        Assert.Contains(Playbook, _writer.Last!.Facts); // «دليل التعامل» is read by the writer, never sent
        Assert.DoesNotContain(d.Actions, a => a.Text?.Contains("دليل التعامل", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task A_doubtful_free_answer_sends_the_approved_words()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش، القانون بيحميك والعقوبة حبس سنة."; // legal words and a number not in the facts
        var d = await Say("انا خايف مراتي تعرف");
        Assert.Equal([TextKeys.FreeAnswer], Keys(d));
        Assert.Equal("معلش، ممكن توضّحلي أكتر حضرتك محتاج إيه؟ وأنا معاك 🙏", d.Actions[^1].Text);
        Assert.NotEqual(WrittenReply.Passed, d.Effects!.Written!.Guard);
    }

    [Fact]
    public async Task Free_answers_never_hand_the_chat_to_the_office_while_the_AI_reads_the_words()
    {
        FreeAnswerOn();
        await ToTheOffer();
        _writer.Reply = "متقلقش يا فندم 🙏 كل كلام حضرتك مع المكتب سري.";
        await Say("انا خايف مراتي تعرف");
        _writer.Reply = "ولا يهمك 🙏 المستشار هو اللي هيسمعك.";
        _script["بجد خايف"] = new("other", null, "unknown", null, 0.8);
        Assert.Equal([TextKeys.FreeAnswer], Keys(await Say("بجد خايف"))); // not «هبلّغ المكتب»
    }

    [Fact]
    public async Task With_the_switch_off_the_playbook_never_reaches_the_writer_and_unclear_is_as_before()
    {
        FreeAnswerOn();
        _settings[UnderstandingV2.SwitchKey] = "false";
        await ToTheOffer();
        var d = await Say("انا خايف مراتي تعرف");
        Assert.DoesNotContain(TextKeys.FreeAnswer, Keys(d));
        Assert.DoesNotContain(Playbook, _writer.Last?.Facts ?? "");
    }
}
