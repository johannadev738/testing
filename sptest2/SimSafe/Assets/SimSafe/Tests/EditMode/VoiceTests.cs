using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SimSafe.Core;

namespace SimSafe.Tests
{
    [TestFixture]
    public sealed class VoiceTests
    {
        ResponseIntention Response(ResponseIntentionType type = ResponseIntentionType.Speak)
        {
            var turn = new CharacterTurn(Guid.NewGuid(), Guid.NewGuid(), ScenarioCategory.KAI, "hello", 3, 12, false);
            return new ResponseIntention(turn, type, "profile.ready", type == ResponseIntentionType.Wait ? "" : "Please give me time.",
                type == ResponseIntentionType.Wait ? 12 : 0);
        }

        sealed class Provider : ITextToSpeechProvider
        {
            public TaskCompletionSource<PcmAudio> Pending = new TaskCompletionSource<PcmAudio>();
            public CancellationToken Token;
            public int Calls;
            public bool ThrowOnCancel;
            public Task<PcmAudio> SynthesizeAsync(string text, CancellationToken token)
            {
                Token = token; Calls++;
                if (ThrowOnCancel) token.Register(() => { throw new InvalidOperationException("Provider cancellation failure"); });
                return Pending.Task;
            }
        }

        static PcmAudio Audio() => new PcmAudio(new[] { -1f, -0.5f, 0f, 0.5f, 1f }, 16000, 1);

        [Test] public void VoiceActivityMetadataRejectsInvalidIdsAndNeverContainsTranscript()
        {
            var id = Guid.NewGuid();
            var payload = new VoiceActivityPayload(id, null, VoiceActivityKind.MicrophoneStarted);
            Assert.AreEqual(id, payload.OperationId); Assert.IsNull(payload.TurnId);
            Assert.AreEqual("none", payload.DetailId);
            Assert.AreEqual(SimulationEventType.VoiceActivityObserved, payload.Type);
            var confirmed = new VoiceActivityPayload(id, Guid.NewGuid(), VoiceActivityKind.TranscriptConfirmed);
            Assert.IsTrue(confirmed.TurnId.HasValue);
            Assert.Throws<ArgumentException>(() => new VoiceActivityPayload(Guid.Empty, null, VoiceActivityKind.ReplyStarted));
            Assert.Throws<ArgumentException>(() => new VoiceActivityPayload(id, Guid.Empty, VoiceActivityKind.ReplyStarted));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VoiceActivityPayload(id, null, (VoiceActivityKind)999));
            Assert.Throws<ArgumentException>(() => new VoiceActivityPayload(id, null, VoiceActivityKind.Failed, "free form transcript"));
        }

        [Test] public void WaveRoundTripPreservesRateChannelsAndSamplePrecision()
        {
            var original = new PcmAudio(new[] { -1f, 1f, 0.25f, -0.5f }, 22050, 2);
            var restored = PcmWaveCodec.Decode(PcmWaveCodec.Encode(original));
            Assert.AreEqual(22050, restored.SampleRate); Assert.AreEqual(2, restored.Channels);
            var samples = restored.CopySamples(); var expected = original.CopySamples();
            for (int i = 0; i < samples.Length; i++) Assert.LessOrEqual(Math.Abs(samples[i] - expected[i]), 1d / 32767);
        }

        [Test] public void WaveCodecDoesNotMutateOrExposeSourceBuffers()
        {
            var bytes = PcmWaveCodec.Encode(Audio()); var original = (byte[])bytes.Clone();
            var restored = PcmWaveCodec.Decode(bytes); bytes[44] = 0;
            Assert.AreEqual(-1f, restored.CopySamples()[0]);
            Assert.AreEqual(original.Length, bytes.Length);
        }

        [Test] public void WaveCodecRejectsNullTruncatedAndInvalidHeaders()
        {
            Assert.Throws<ArgumentNullException>(() => PcmWaveCodec.Encode(null));
            Assert.Throws<ArgumentNullException>(() => PcmWaveCodec.Decode(null));
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(new byte[43]));
            var bytes = PcmWaveCodec.Encode(Audio()); bytes[0] = 0;
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
            bytes = PcmWaveCodec.Encode(Audio()); bytes[8] = 0;
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
        }

        [Test] public void WaveCodecRejectsUnsupportedFormatRateChannelsAndBits()
        {
            foreach (int offset in new[] { 20, 22, 24, 34 })
            {
                var bytes = PcmWaveCodec.Encode(Audio()); bytes[offset] = 0; bytes[offset + 1] = 0;
                Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
            }
        }

        [Test] public void WaveCodecRejectsOversizedRiffAndChunkLengths()
        {
            var bytes = PcmWaveCodec.Encode(Audio());
            for (int i = 4; i < 8; i++) bytes[i] = 255;
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
            bytes = PcmWaveCodec.Encode(Audio());
            for (int i = 40; i < 44; i++) bytes[i] = 255;
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
        }

        [Test] public void WaveCodecRejectsEmptyOrIncompleteFramesAndTruncatedData()
        {
            var bytes = PcmWaveCodec.Encode(Audio()); bytes[40] = 0;
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
            bytes = PcmWaveCodec.Encode(Audio()); bytes[40] = 9;
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
            bytes = PcmWaveCodec.Encode(Audio()); Array.Resize(ref bytes, bytes.Length - 1);
            Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
        }

        [Test] public void WaveCodecRejectsInconsistentBlockAndByteRates()
        {
            foreach (int offset in new[] { 28, 32 })
            {
                var bytes = PcmWaveCodec.Encode(Audio()); bytes[offset]++;
                Assert.Throws<InvalidDataException>(() => PcmWaveCodec.Decode(bytes));
            }
        }

        [Test] public void GateRejectsEmptyIdsAndInvalidatesOldTurns()
        {
            var gate = new VoiceTurnGate(); var session = Guid.NewGuid(); var turn = Guid.NewGuid();
            Assert.Throws<ArgumentException>(() => gate.Begin(Guid.Empty, turn));
            Assert.Throws<ArgumentException>(() => gate.Begin(session, Guid.Empty));
            var first = gate.Begin(session, turn); Assert.IsTrue(gate.IsCurrent(first, session, turn));
            var second = gate.Begin(session, turn); Assert.IsFalse(gate.IsCurrent(first, session, turn));
            Assert.IsTrue(gate.IsCurrent(second, session, turn));
            Assert.IsFalse(gate.IsCurrent(second, Guid.NewGuid(), turn));
            gate.Cancel(); Assert.IsFalse(gate.IsCurrent(second, session, turn));
        }

        [Test] public void SynthesisReturnsCurrentResultAndWaitDoesNotCallProvider()
        {
            var provider = new Provider(); using (var controller = new VoiceSynthesisController(provider))
            {
                Assert.IsNull(controller.SynthesizeAsync(Response(ResponseIntentionType.Wait), CancellationToken.None).GetAwaiter().GetResult());
                Assert.AreEqual(0, provider.Calls);
                var task = controller.SynthesizeAsync(Response(), CancellationToken.None); var audio = Audio();
                provider.Pending.SetResult(audio); Assert.AreEqual(audio, task.GetAwaiter().GetResult());
                Assert.AreEqual(1, provider.Calls);
            }
        }

        [Test] public void SupersededSynthesisIsDiscardedEvenIfProviderIgnoresCancellation()
        {
            var provider = new Provider(); using (var controller = new VoiceSynthesisController(provider))
            {
                var old = controller.SynthesizeAsync(Response(), CancellationToken.None); var oldPending = provider.Pending;
                var oldToken = provider.Token; provider.Pending = new TaskCompletionSource<PcmAudio>();
                var current = controller.SynthesizeAsync(Response(), CancellationToken.None);
                Assert.IsTrue(oldToken.IsCancellationRequested);
                oldPending.SetResult(Audio()); Assert.IsNull(old.GetAwaiter().GetResult());
                var audio = Audio(); provider.Pending.SetResult(audio); Assert.AreEqual(audio, current.GetAwaiter().GetResult());
            }
        }

        [Test] public void CancelAndDisposePreventLateAudio()
        {
            var provider = new Provider(); var controller = new VoiceSynthesisController(provider);
            var task = controller.SynthesizeAsync(Response(), CancellationToken.None);
            controller.Cancel(); Assert.IsTrue(provider.Token.IsCancellationRequested);
            provider.Pending.SetResult(Audio()); Assert.IsNull(task.GetAwaiter().GetResult());
            controller.Dispose(); controller.Dispose();
            Assert.Throws<ObjectDisposedException>(() => controller.SynthesizeAsync(Response(), CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test] public void CallerCancellationPreventsUncooperativeProviderResult()
        {
            var provider = new Provider(); using (var controller = new VoiceSynthesisController(provider))
            using (var cancellation = new CancellationTokenSource())
            {
                var task = controller.SynthesizeAsync(Response(), cancellation.Token);
                cancellation.Cancel(); provider.Pending.SetResult(Audio());
                Assert.IsNull(task.GetAwaiter().GetResult());
            }
        }

        [Test] public void ProviderCancellationFailureCannotBlockAReplacementRequest()
        {
            var provider = new Provider { ThrowOnCancel = true };
            using (var controller = new VoiceSynthesisController(provider))
            {
                var old = controller.SynthesizeAsync(Response(), CancellationToken.None); var oldPending = provider.Pending;
                provider.ThrowOnCancel = false; provider.Pending = new TaskCompletionSource<PcmAudio>();
                var replacement = controller.SynthesizeAsync(Response(), CancellationToken.None);
                oldPending.SetResult(Audio()); Assert.IsNull(old.GetAwaiter().GetResult());
                var audio = Audio(); provider.Pending.SetResult(audio); Assert.AreEqual(audio, replacement.GetAwaiter().GetResult());
            }
        }

        [Test] public void AlreadyCancelledCallerDoesNotInvokeSpeechProvider()
        {
            var provider = new Provider(); using (var controller = new VoiceSynthesisController(provider))
            {
                Assert.IsNull(controller.SynthesizeAsync(Response(), new CancellationToken(true)).GetAwaiter().GetResult());
                Assert.AreEqual(0, provider.Calls);
            }
        }

        [Test] public void NullInputsAndProviderResultsAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new VoiceSynthesisController(null));
            var provider = new Provider(); using (var controller = new VoiceSynthesisController(provider))
            {
                Assert.Throws<ArgumentNullException>(() => controller.SynthesizeAsync(null, CancellationToken.None).GetAwaiter().GetResult());
                var task = controller.SynthesizeAsync(Response(), CancellationToken.None); provider.Pending.SetResult(null);
                Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            }
        }
    }
}
