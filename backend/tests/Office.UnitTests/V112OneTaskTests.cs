using Office.Application.Booking;
using Office.Application.Conversations;
using Office.Application.Responders;
using Office.Application.Texts;
using Office.Application.Turns;
using Office.Application.Writing;

namespace Office.UnitTests;

/// <summary>
/// DECISIONS v1.112 (the owner, 2026-11-14 10:41, of a bell full of one client's tasks: «ليه في كل النوتيفيكيشنز دي؟ … انت مش
/// حاسس ان الدنيا كده ملعبكة؟» … «ماشي تمام اعمل كدا»). What Hazem decides here: an answer the office facts do not give is
/// refused (his question reaches the office), his words about an open request are his words to the office (never its status
/// in their place), the words with his papers go with the papers' task, and what follows the written reply is never said in it.
/// One task per client for the same job is the database's (the integration tests).
/// </summary>
public sealed class V112ReplyGuardTests
{
    private const string Privacy = "أكيد يا فندم 🔒 كل كلام حضرتك مع المكتب سري تمامًا، والمكتب عمره ما بيفشي أسرار عملائه.";

    private static WriteRequest Request(string after) =>
        new("purpose", "صياغة العقد بتبدأ حسب نوعه، والمستشار بيحدد السعر النهائي.", "", []) { After = after };

    [Fact]
    public void What_goes_right_after_the_written_reply_is_never_said_in_it()
    {
        // Ahmed Amin's chat, 10:24: the written reply said his words stay private, and the approved privacy line followed it
        Assert.Equal("repeats_after", ReplyGuard.Problem("تمام يا فندم، وكلام حضرتك سري وبيفضل عند المكتب بس.", Request(Privacy), null));
        Assert.Null(ReplyGuard.Problem("تمام يا فندم، والمستشار بيحدد السعر النهائي بعد ما يشوف الورق.", Request(Privacy), null));
        Assert.Null(ReplyGuard.Problem("تمام يا فندم، وكلام حضرتك سري وبيفضل عند المكتب بس.", Request(""), null)); // nothing after it
    }

    [Theory]
    [InlineData("سؤال حضرتك وصل للمكتب، وهيردوا عليك هنا.", true)]
    [InlineData("تمام، ابعت البيانات اللي عندك هنا. سؤالك عن طريقة السداد هوصّله للمكتب، ويردوا على حضرتك هنا 👌", true)]
    [InlineData("تمام، وصلتني التفاصيل 👍 وسؤال الشرط الجزائي هوصّلها للمستشار مع الطلب.", true)]
    [InlineData("المطلوب منك في عقد البيع: أسماء الطرفين وبطاقاتهم، وتفاصيل الشيء المبيع.", false)]
    public void A_reply_says_the_office_will_answer_his_question(string reply, bool says) =>
        Assert.Equal(says, Conversational.SaysOfficeAnswers(reply));
}

public sealed partial class BookingFlowTests
{
    private const string Penalty = "العربية هي تويوتا كورولا موديل 2022، والمشتري هيدفع مقدم والباقي على شهور، ممكن تضيف شرط جزائي؟";

    [Fact]
    public async Task V112_details_and_a_question_about_the_open_request_reach_the_office_never_its_status()
    {
        // Ahmed Amin's chat, 10:16: read as naming the service again, it got «طلب حضرتك مستني الورق», and the question was lost
        await AhmedAsksForAContract();
        await Press(ServiceRow.Id(ServiceRow.Drafting));
        _script[Penalty] = new("service_by_name", "contract_drafting", "unknown", null, 0.95);
        var d = await Say(Penalty);
        Assert.Equal([TextKeys.ServiceDetail], Keys(d));
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(d)); // it reaches the office
        Assert.DoesNotContain(d.Actions, a => a.Text?.Contains("مستني الورق", StringComparison.Ordinal) == true);
        Assert.Single(_booking.Orders); // nothing started again

        // his request's status when he asks for it (v1.43)
        _script["طلبي وصل لفين؟"] = new("my_status", "contract_drafting", "unknown", null, 0.95);
        Assert.Equal([TextKeys.SituationOrder], Keys(await Say("طلبي وصل لفين؟")));
    }

    [Fact]
    public async Task V112_an_answer_the_office_facts_do_not_give_is_refused_and_his_question_reaches_the_office()
    {
        // Ahmed Amin's chat, 10:10–10:28: four answers no office fact gave (the papers a sale contract needs), and four tasks
        await AhmedAsksForAContract(writer: true);
        await Press(ServiceRow.Id(ServiceRow.Drafting));
        _writer.FromFacts = false;
        _writer.Reply = "المطلوب منك: صورة بطاقة المشتري، ورقم الشاسيه، ورقم اللوحة.";
        var alone = await Say("ورق زي ايه مش معايا ورق انا");
        Assert.Equal("answered_alone", alone.Effects!.Written!.Guard);
        Assert.Equal("سؤال حضرتك وصل للمكتب، وهيردوا عليك هنا.", Assert.Single(alone.Actions).Text); // the approved words
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(alone));

        _writer.Reply = "تمام يا أستاذ، سؤال حضرتك وصل للمكتب، وهيردوا عليك هنا 👌";
        var passed = await Say("ورق زي ايه مش معايا ورق انا");
        Assert.Equal(WrittenReply.Passed, passed.Effects!.Written!.Guard);
        Assert.Contains(PendingTask.ServiceQuestion, TaskKinds(passed));

        // answered from the office facts: no task (v1.48)
        _writer.FromFacts = true;
        _writer.Reply = "أيوه يا أستاذ، تقدر تبعت الورق بعدين.";
        var facts = await Say("ورق زي ايه مش معايا ورق انا");
        Assert.Equal(WrittenReply.Passed, facts.Effects!.Written!.Guard);
        Assert.DoesNotContain(PendingTask.ServiceQuestion, TaskKinds(facts));
    }

    [Fact]
    public async Task V112_words_with_the_papers_go_with_their_pricing_task_never_as_unclear()
    {
        // Ahmed Amin's chat, 10:28: the ID photo «دي صوره البطاقه» and the car's details got «رسالة غير واضحة» beside the pricing
        await AhmedAsksForAContract();
        await Press(ServiceRow.Id(ServiceRow.Drafting));
        var d = await Send(new Turn($"wt:{++_turn}", Number, null,
        [
            new IncomingMessage($"wamid.{++_turn}", MessageType.Image, null, null, null, Caption: "دي صوره البطاقه",
                Media: new MediaRef(MediaStatus.Stored, "office-sensitive", $"k{_turn}", "image/jpeg", 1000, $"sha{_turn}", "card")),
            new IncomingMessage($"wamid.{++_turn}", MessageType.Text, "بيانات العربية: تويوتا كورولا موديل 2022", null, null),
        ]));
        Assert.Equal([TextKeys.ServiceFilesReceived], Keys(d));
        Assert.Equal([PendingTask.ServicePricing], TaskKinds(d)); // his words are on its page (the turn's words)
    }
}
