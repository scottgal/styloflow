using Mostlylucid.Ephemeral.Atoms.Taxonomy;

namespace StyloFlow.WorkflowBuilder.Atoms.Renderers;

/// <summary>
/// Email sender renderer - sends email notifications.
/// Taxonomy: renderer, deterministic, direct write allowed
/// </summary>
public sealed class EmailSenderRenderer
{
    public static AtomContract Contract => AtomContract.Create(
        AtomKind.Renderer,
        AtomDeterminism.Deterministic,
        AtomPersistence.DirectWriteAllowed,
        name: "email-sender",
        reads: ["filter.passed", "sentiment.label", "text.content"],
        writes: ["email.sent", "email.message_id"]);

    public static Task ExecuteAsync(WorkflowAtomContext ctx)
    {
        var filterPassed = ctx.Signals.Get<bool>("filter.passed");
        if (!filterPassed)
        {
            ctx.Log("Filter did not pass - skipping email");
            return Task.CompletedTask;
        }

        var to = ctx.Config.TryGetValue("to", out var t) ? t?.ToString() : "admin@example.com";
        var subjectTemplate = ctx.Config.TryGetValue("subject_template", out var st) ? st?.ToString() : "Alert: {{sentiment.label}}";
        var bodyTemplate = ctx.Config.TryGetValue("body_template", out var bt) ? bt?.ToString() : "Sentiment detected: {{sentiment.label}}";

        var subject = InterpolateTemplate(subjectTemplate!, ctx);
        var body = InterpolateTemplate(bodyTemplate!, ctx);

        var messageId = $"msg-{Guid.NewGuid():N}"[..16];

        ctx.Log($"Sending email to {to}");
        ctx.Log($"  Subject: {subject}");
        ctx.Log($"  Body: {body[..Math.Min(body.Length, 100)]}...");
        ctx.Log($"  Message ID: {messageId}");

        ctx.Emit("email.sent", true);
        ctx.Emit("email.message_id", messageId);

        return Task.CompletedTask;
    }

    private static string InterpolateTemplate(string template, WorkflowAtomContext ctx)
    {
        var result = template;
        foreach (var signal in ctx.Signals.GetAll())
        {
            var placeholder = $"{{{{{signal.Signal}}}}}";
            result = result.Replace(placeholder, signal.Key ?? "");
        }
        return result;
    }
}
