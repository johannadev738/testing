using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimSafe.Core
{
    /// <summary>Correlates asynchronous voice results; IDs alone cannot distinguish a retried request.</summary>
    public sealed class VoiceTurnGate
    {
        readonly object sync = new object();
        long version;
        Guid sessionId, turnId;
        public long Begin(Guid session, Guid turn)
        {
            if (session == Guid.Empty || turn == Guid.Empty) throw new ArgumentException("Voice requests require session and turn IDs.");
            lock (sync) { sessionId = session; turnId = turn; return ++version; }
        }
        public bool IsCurrent(long requestVersion, Guid session, Guid turn)
        {
            lock (sync) return sessionId != Guid.Empty && version == requestVersion && sessionId == session && turnId == turn;
        }
        public void Cancel()
        {
            lock (sync) { ++version; sessionId = turnId = Guid.Empty; }
        }
    }

    /// <summary>Latest-result-wins synthesis; no Unity playback, event claims or automatic persistence.</summary>
    public sealed class VoiceSynthesisController : IDisposable
    {
        readonly ITextToSpeechProvider provider;
        readonly VoiceTurnGate gate = new VoiceTurnGate();
        readonly object sync = new object();
        CancellationTokenSource current;
        bool disposed;

        public VoiceSynthesisController(ITextToSpeechProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public async Task<PcmAudio> SynthesizeAsync(ResponseIntention intention, CancellationToken cancellationToken)
        {
            if (intention == null) throw new ArgumentNullException(nameof(intention));
            CancellationTokenSource previous, request;
            long version;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(VoiceSynthesisController));
                previous = current;
                gate.Cancel();
                if ((intention.Type != ResponseIntentionType.Speak && intention.Type != ResponseIntentionType.RequestSpace) ||
                    string.IsNullOrWhiteSpace(intention.Text))
                {
                    current = null; request = null; version = 0;
                }
                else
                {
                    request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    current = request; version = gate.Begin(intention.SessionId, intention.TurnId);
                }
            }
            CancelSafely(previous);
            if (request == null) return null;
            try
            {
                if (request.IsCancellationRequested) return null;
                var audio = await provider.SynthesizeAsync(intention.Text, request.Token).ConfigureAwait(false);
                if (request.IsCancellationRequested || !gate.IsCurrent(version, intention.SessionId, intention.TurnId)) return null;
                if (audio == null) throw new InvalidOperationException("Speech provider returned no audio.");
                return audio;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { return null; }
            catch (Exception) when (!gate.IsCurrent(version, intention.SessionId, intention.TurnId)) { return null; }
            finally
            {
                lock (sync) { if (ReferenceEquals(current, request)) current = null; }
                request.Dispose();
            }
        }

        public void Cancel()
        {
            CancellationTokenSource pending;
            lock (sync) { gate.Cancel(); pending = current; current = null; }
            CancelSafely(pending);
        }

        public void Dispose()
        {
            lock (sync) { disposed = true; }
            Cancel();
        }

        static void CancelSafely(CancellationTokenSource cancellation)
        {
            if (cancellation == null) return;
            try { cancellation.Cancel(); }
            catch (ObjectDisposedException) { }
            // A provider's cancellation callback must not block the next turn or teardown.
            catch (AggregateException) { }
        }
    }
}
