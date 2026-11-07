namespace HeadlessScreenReader.Worker;

/// <summary>One bounded child-control conversation, joined with native process completion.</summary>
internal interface IWorkerProgressExchange
{
    Task StartAsync(Stream input, CancellationToken token);
    Task ReadAsync(Stream progress, Stream acknowledgements, CancellationTokenSource stop);
}
