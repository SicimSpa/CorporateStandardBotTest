using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Search.Documents.KnowledgeBases;
using Azure.Search.Documents.KnowledgeBases.Models;
using CorporateStandardBotTest.BusinessLogic.Models;
using Lena.Asyncs;
using Lena.Core;
using Microsoft.Extensions.Logging;

namespace CorporateStandardBotTest.BusinessLogic.Services;

public interface IKnowledgeBaseService
{
    Task<Result<AiChatMessage>> GetResponseAsync(AiCompletionRequest chat, string? userEmail);
}

public partial class KnowledgeBaseService(ILogger<KnowledgeBaseService> logger, IKnowledgeBaseUrlService knowledgeBaseUrlService, KnowledgeBaseRetrievalClient kbClient)
    : IKnowledgeBaseService
{
    public async Task<Result<AiChatMessage>> GetResponseAsync(AiCompletionRequest request, string? userEmail)
    {
        KnowledgeRetrievalReasoningEffort effort = request.ReasoningEffort switch
        {
            AiReasoningEffort.None => new KnowledgeRetrievalMinimalReasoningEffort(),
            AiReasoningEffort.Low => new KnowledgeRetrievalLowReasoningEffort(),
            AiReasoningEffort.Medium => new KnowledgeRetrievalMediumReasoningEffort(),
            _ => throw new ArgumentOutOfRangeException()
        };

        var chat = request.Chat;

        const string systemMessage =
            "A Q&A agent that can answer questions about SICIM corporate policies.\nIf you don't have the answer, respond with \"I don't know\".";

        var retrievalRequest = new KnowledgeBaseRetrievalRequest
        {
            RetrievalReasoningEffort = effort,
            OutputMode = KnowledgeRetrievalOutputMode.AnswerSynthesis
        };

        if (request.ReasoningEffort == AiReasoningEffort.None)
        {
            retrievalRequest.Intents.Add(
                new KnowledgeRetrievalSemanticIntent(chat.Messages.LastOrDefault(x => x.Role == AiMessageRole.User)
                    ?.Content));
        }
        else
        {
            retrievalRequest.Messages.Add(
                new KnowledgeBaseMessage(content: [new KnowledgeBaseMessageTextContent(systemMessage)])
                    { Role = "system" });

            foreach (var message in chat.Messages)
            {
                var role = message.Role switch
                {
                    AiMessageRole.User => "user",
                    AiMessageRole.Assistant => "assistant",
                    _ => throw new ArgumentOutOfRangeException()
                };
                retrievalRequest.Messages.Add(new KnowledgeBaseMessage(content:
                    [new KnowledgeBaseMessageTextContent(message.Content)]) { Role = role });
            }
        }
        
        var threadId = request.Chat.ThreadId;
        var messageId = chat.Messages.LastOrDefault(x => x.Role == AiMessageRole.User)?.MessageId;

        Activity.Current?
            .AddTag("knowledgeBase.getResponse.userEmail", userEmail)
            .AddTag("knowledgeBase.getResponse.threadId", threadId)
            .AddTag("knowledgeBase.getResponse.messageId", messageId);

        var result = await ResultAsync.TryAsync(async () => await kbClient.RetrieveAsync(retrievalRequest));

        var returnValue = result
            .Match(
                success: response =>
                {
                    var queryPlannings =
                        response.Value.Activity.OfType<KnowledgeBaseModelQueryPlanningActivityRecord>().ToArray();

                    var queryPlanningDuration = queryPlannings.Sum(x => x.ElapsedMs ?? 0);
                    var queryPlanningInputTokens = queryPlannings.Sum(x => x.InputTokens ?? 0);
                    var queryPlanningOutputTokens = queryPlannings.Sum(x => x.OutputTokens ?? 0);

                    var retrievals = response.Value.Activity.OfType<KnowledgeBaseRetrievalActivityRecord>().ToArray();

                    var retrievalDuration = retrievals.Sum(x => x.ElapsedMs ?? 0);
                    var retrievalTotalDocuments = retrievals.Sum(x => x.Count ?? 0);
                    var retrievalDocumentsByKnowledgeBase = retrievals.GroupBy(x => x.KnowledgeSourceName)
                        .ToDictionary(x => x.Key, x => x.Sum(y => y.Count ?? 0));

                    var agenticReasonings = response.Value.Activity
                        .OfType<KnowledgeBaseAgenticReasoningActivityRecord>()
                        .Select(x =>
                        {
                            var effortLevel = x.RetrievalReasoningEffort switch
                            {
                                KnowledgeRetrievalMinimalReasoningEffort => "minimal",
                                KnowledgeRetrievalLowReasoningEffort => "low",
                                KnowledgeRetrievalMediumReasoningEffort => "medium",
                                _ => "unknown"
                            };

                            return new { Label = effortLevel, ReasoningActivity = x };
                        })
                        .ToArray();

                    var agenticReasoningDuration = agenticReasonings.Sum(x => x.ReasoningActivity.ElapsedMs ?? 0);
                    var agenticReasoningTokens = agenticReasonings.Sum(x => x.ReasoningActivity.ReasoningTokens ?? 0);
                    var agenticReasoningLevel = agenticReasonings.Select(x => x.Label).FirstOrDefault();

                    var answerSynthesis = response.Value.Activity
                        .OfType<KnowledgeBaseModelAnswerSynthesisActivityRecord>()
                        .ToArray();

                    var answerSynthesisDuration = answerSynthesis.Sum(x => x.ElapsedMs ?? 0);
                    var answerSynthesisInputTokens = answerSynthesis.Sum(x => x.InputTokens ?? 0);
                    var answerSynthesisOutputTokens = answerSynthesis.Sum(x => x.OutputTokens ?? 0);

                    var totalInputTokens = queryPlanningInputTokens + answerSynthesisInputTokens;
                    var totalOutputTokens = queryPlanningOutputTokens + answerSynthesisOutputTokens;
                    var totalReasoningTokens = agenticReasoningTokens;
                    var totalOutputAndReasoningTokes = totalReasoningTokens + totalOutputTokens;

                    var totalAzureSearchDuration = queryPlanningDuration + retrievalDuration +
                                                   agenticReasoningDuration + answerSynthesisDuration;

                    Activity.Current?
                        .AddTag("knowledgeBase.getResponse.queryPlanningDuration", queryPlanningDuration)
                        .AddTag("knowledgeBase.getResponse.queryPlanningInputTokens", queryPlanningInputTokens)
                        .AddTag("knowledgeBase.getResponse.queryPlanningOutputTokens", queryPlanningOutputTokens)
                        .AddTag("knowledgeBase.getResponse.retrievalDuration", retrievalDuration)
                        .AddTag("knowledgeBase.getResponse.retrievalTotalDocuments", retrievalTotalDocuments)
                        .AddTag("knowledgeBase.getResponse.agenticReasoningDuration", agenticReasoningDuration)
                        .AddTag("knowledgeBase.getResponse.agenticReasoningTokens", agenticReasoningTokens)
                        .AddTag("knowledgeBase.getResponse.agenticReasoningLevel", agenticReasoningLevel)
                        .AddTag("knowledgeBase.getResponse.answerSynthesisDuration", answerSynthesisDuration)
                        .AddTag("knowledgeBase.getResponse.answerSynthesisInputTokens", answerSynthesisInputTokens)
                        .AddTag("knowledgeBase.getResponse.answerSynthesisOutputTokens", answerSynthesisOutputTokens)
                        .AddTag("knowledgeBase.getResponse.totalInputTokens", totalInputTokens)
                        .AddTag("knowledgeBase.getResponse.totalOutputTokens", totalOutputTokens)
                        .AddTag("knowledgeBase.getResponse.totalReasoningTokens", totalReasoningTokens)
                        .AddTag("knowledgeBase.getResponse.totalOutputAndReasoningTokes", totalOutputAndReasoningTokes)
                        .AddTag("knowledgeBase.getResponse.totalAzureSearchDuration", totalAzureSearchDuration);

                    if (Activity.Current is not null)
                    {
                        foreach (var (knowledgeBaseName, documentCount) in retrievalDocumentsByKnowledgeBase)
                        {
                            Activity.Current
                                .AddTag($"knowledgeBase.getResponse.retrieval.knowledgeBase.{knowledgeBaseName}.documentCount",
                                    documentCount);
                        }
                    }

                    logger.LogInformation(
                        "Chat completion with message id {messageId}, thread id {threadId} for user {email} with {queryPlanningInputTokens} input tokens took {totalAzureSearchDuration} ms and generated {totalOutputTokens} output tokens ({totalOutputAndReasoningTokes} with reasonig)",
                        messageId, threadId, userEmail, queryPlanningInputTokens, totalAzureSearchDuration,
                        totalOutputTokens, totalOutputAndReasoningTokes);

                    var text = (response.Value.Response[0].Content[0] as KnowledgeBaseMessageTextContent)!.Text;

                    var refsRegex = KnowledgeBaseRefRegex();
                    var refsInText = refsRegex.Matches(text).Select(x => x.Groups[1].Value).ToHashSet();
                    
                    var references = response.Value.References
                        .OfType<KnowledgeBaseAzureBlobReference>()
                        .Where(x => refsInText.Contains(x.Id))
                        .Select(x => new AiChatReference(x.Id, knowledgeBaseUrlService.GetSignedUrl(x.BlobUrl), GetFileName(x.BlobUrl)))
                        .ToList();

                    return Result.Success(new AiChatMessage(Guid.NewGuid(), AiMessageRole.Assistant, text, references));
                },
                error: ex =>
                {
                    Activity.Current?
                        .AddException(ex)
                        .SetStatus(ActivityStatusCode.Error);

                    return Result<AiChatMessage>.Error("Unable to generate a response");
                });

        return returnValue;
    }

    private async Task<Result<AiChatMessage>> GetFakeResponseAsync(AiChat chat)
    {
        await Task.Delay(5000);
        var chatMessage = JsonSerializer.Deserialize<AiChatMessage>("""
                                                                    {"Role":1,"Content":"Ecco una panoramica pratica sui rimborsi spese per dipendenti SICIM, basata sulle informazioni disponibili:\n\nRequisiti documentali\n- Occorre compilare una nota spese (cartacea o digitale) con tutte le spese sostenute durante trasferta/missione e documentarle con fatture, ricevute o scontrini fiscali [ref_id:0][ref_id:4].\n- Sono accettati anche semplici scontrini, note o conti quando non \u00E8 possibile ottenere fattura o ricevuta fiscale; i giustificativi devono riportare data, generalit\u00E0 (ditta/ragione sociale/residenza) e importo pagato [ref_id:0][ref_id:7].\n- Per biglietti di treno, pedaggi autostradali, parcheggi, taxi, mezzi pubblici, bar e servizi sono considerati idonei i relativi biglietti o scontrini rilasciati dall\u2019erogatore del servizio [ref_id:7].\n- I pagamenti devono essere preferibilmente tracciati (carte di debito/credito o pagamenti elettronici); spese pagate in contanti non rimborsabili se non tracciate [ref_id:7].\n- Le ricevute devono essere conservate per il rimborso; per i pedaggi \u00E8 richiesto il relativo pagamento elettronico o ricevuta [ref_id:1][ref_id:3][ref_id:9].\n\nProcedura passo\u2011passo per presentare il rimborso\n1) Conservare tutti i giustificativi di spesa durante la trasferta/missione (fatture, ricevute, scontrini, biglietti) [ref_id:0][ref_id:7].\n2) Compilare la nota spese (paper o digitale) includendo solo le spese funzionali alla trasferta/missione [ref_id:4].\n3) Far validare le spese dal proprio responsabile e comunicare la richiesta all\u2019ufficio amministrazione per procedere al rimborso [ref_id:1][ref_id:6].\n4) Inviare eventuali moduli o documenti richiesti (es. per incidenti con veicoli aziendali compilare modulo CID e inviarlo entro 24 ore alle figure indicate) quando pertinenti [ref_id:1][ref_id:6].\n\nTempistiche\n- Le ricevute presentate oltre 3 mesi dalla fine della trasferta non sono rimborsabili salvo diversa indicazione per personale locale; quindi presentare le richieste entro 3 mesi dalla trasferta [ref_id:8].\n- Per fornitori esterni (es. servizi di smaltimento rifiuti) il pagamento fatture avviene entro 60 giorni fine mese data fattura, dopo invio della documentazione richiesta (es. quarta copia del Formulario) \u2014 questo vale per fornitori, non per dipendenti [ref_id:2][ref_id:5].\n\nModalit\u00E0 di pagamento\n- I rimborsi sono erogati dall\u2019amministrazione dopo validazione del responsabile e comunicazione all\u2019ufficio amministrazione [ref_id:1][ref_id:8].\n- Per il personale di sede (Headquarters) l\u2019autorizzazione per il rimborso \u00E8 richiesta anche dal Responsabile di Dipartimento e dalla Direzione H.R. prima dell\u2019emissione [ref_id:8].\n\nLimiti, esclusioni e regole operative\n- Non sono rimborsabili spese extra non necessarie al viaggio (es. upgrade posti, priority, ecc.) e qualsiasi consumo/ acquisto di alcol non sar\u00E0 rimborsato in ottemperanza alla Politica Alcool Free [ref_id:8].\n- Non sono rimborsabili percorrenze con auto privata per ritiro documenti/certificati salvo diversa indicazione [ref_id:8].\n- Per l\u2019uso dell\u2019auto personale in missione il rimborso \u00E8 a base chilometrica secondo tariffe ACI; i pedaggi autostradali devono essere documentati da ricevuta e prova di pagamento elettronico [ref_id:9].\n- In caso di multe o sanzioni legate a violazioni del Codice della Strada a carico del dipendente, l\u2019onere del pagamento \u00E8 a carico del trasgressore e potr\u00E0 essere trattenuto dalla retribuzione o altre spettanze [ref_id:3].\n- Eventuali spese per danni al veicolo aziendale sono normalmente a carico della societ\u00E0, salvo dolo o colpa grave dell\u2019assegnatario o uso non lavorativo [ref_id:6].\n\nContatti utili\n- Ufficio Amministrazione: riceve le richieste di rimborso (le spese validate dal responsabile vanno comunicate all\u2019ufficio amministrazione) [ref_id:1].\n- Direzione H.R.: per autorizzazioni finali dei rimborsi per il personale di sede e per disposizioni sulla non rimborsabilit\u00E0 oltre 3 mesi [ref_id:8].\n- Country Manager / Project Manager: autorizzano pagamenti eccezionali locali e gestiscono situazioni particolari in missione/distacco [ref_id:8].\n- Dipartimento HSE: destinatario della quarta copia del Formulario di trasporto rifiuti quando rilevante per fornitori/servizi connessi [ref_id:2][ref_id:5].\n\nNote finali e avvertenze pratiche\n- Presentare la nota spese completa e i giustificativi entro 3 mesi dalla fine della trasferta per evitare il rifiuto del rimborso [ref_id:8].\n- Usare pagamenti tracciabili durante la trasferta per assicurare il rimborso [ref_id:7].\n- Far validare sempre le spese dal proprio responsabile prima di inviarle all\u2019amministrazione [ref_id:1].\n\nSe desideri, posso riepilogare i moduli e i punti da compilare nella nota spese in formato checklist operativa (campo, documento richiesto, dove inviare).","References":[{"Id":"0","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/8304-SI-001-00_Travel%20policy.docx","Name":"8304-SI-001-00_Travel policy.docx"},{"Id":"1","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/05_Procurement%20\u0026%20Logistics/0530-SI-002%20-%20Corporate%20Vehicle%20Procedure/0530-SI-002-00_Corporate%20Vehicle%20Procedure.pdf","Name":"0530-SI-002-00_Corporate Vehicle Procedure.pdf"},{"Id":"3","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/05_Procurement%20\u0026%20Logistics/0530-SI-002%20-%20Corporate%20Vehicle%20Procedure/0530-SI-002-00_Corporate%20Vehicle%20Procedure.docx","Name":"0530-SI-002-00_Corporate Vehicle Procedure.docx"},{"Id":"6","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/05_Procurement%20\u0026%20Logistics/0530-SI-002%20-%20Corporate%20Vehicle%20Procedure/0530-SI-002-00_Corporate%20Vehicle%20Procedure.docx","Name":"0530-SI-002-00_Corporate Vehicle Procedure.docx"},{"Id":"4","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/ONGOING/8304-SI-001-00_Travel%20policy.NUOVA%20VERSIONE_MF.docx","Name":"8304-SI-001-00_Travel policy.NUOVA VERSIONE_MF.docx"},{"Id":"7","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/ONGOING/8304-SI-001-01_Travel%20policy.NUOVA%20VERSIONE%20(1).docx","Name":"8304-SI-001-01_Travel policy.NUOVA VERSIONE (1).docx"},{"Id":"9","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/ONGOING/8304-SI-001-01_Travel%20policy.NUOVA%20VERSIONE%20(1).docx","Name":"8304-SI-001-01_Travel policy.NUOVA VERSIONE (1).docx"},{"Id":"2","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/09_HSE/E/0904-SI-106%20-%20Gestione%20servizi%20trasporto%20e%20smaltimento%20rifiuti%20%20-%20Sede%20Busseto/Attachments/0906-SI-016-03_Modalit%C3%A0%20di%20fornitura%20del%20servizio%20di%20trasporto%20e%20smaltimento%20rifiuti.docx","Name":"0906-SI-016-03_Modalit\u00E0 di fornitura del servizio di trasporto e smaltimento rifiuti.docx"},{"Id":"5","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/09_HSE/E/0904-SI-106%20-%20Gestione%20servizi%20trasporto%20e%20smaltimento%20rifiuti%20%20-%20Sede%20Busseto/Attachments/0906-SI-016-03_Modalit%C3%A0%20di%20fornitura%20del%20servizio%20di%20trasporto%20e%20smaltimento%20rifiuti.pdf","Name":"0906-SI-016-03_Modalit\u00E0 di fornitura del servizio di trasporto e smaltimento rifiuti.pdf"},{"Id":"8","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/ONGOING/8304-SI-001-00_Travel%20policy.NUOVA%20VERSIONE_MF.docx","Name":"8304-SI-001-00_Travel policy.NUOVA VERSIONE_MF.docx"},{"Id":"10","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/ONGOING/8304-SI-001-00_Travel%20policy.NUOVA%20VERSIONE_MF.docx","Name":"8304-SI-001-00_Travel policy.NUOVA VERSIONE_MF.docx"},{"Id":"11","Url":"https://sicimarchivetest.blob.core.windows.net/test-corporatestandard-ai/root-Archive-260413-1512/83_HR/8304-SI-001%20-%20Travel%20policy/ONGOING/8304-SI-001-00_Travel%20policy.NUOVA%20VERSIONE_MF.docx","Name":"8304-SI-001-00_Travel policy.NUOVA VERSIONE_MF.docx"}]}
                                                                    """)!;

        var r = Result.Success(chatMessage);
        return r;
    }

    private static string GetFileName(string path)
    {
        var filePath = Result.Try(() => new Uri(path).LocalPath)
            .GetOrElse(path);

        var index = filePath.LastIndexOf('/');
        var name = index == -1 ? filePath : filePath[(index + 1)..];
        return name;
    }

    [GeneratedRegex(@"\[ref_id:(\d+)\]")]
    private static partial Regex KnowledgeBaseRefRegex();
}