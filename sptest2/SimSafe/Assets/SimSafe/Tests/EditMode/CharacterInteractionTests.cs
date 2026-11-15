using System;
using System.Threading;
using NUnit.Framework;
using SimSafe.Core;

namespace SimSafe.Tests
{
    [TestFixture]
    public sealed class CharacterInteractionTests
    {
        CharacterProfile Profile(double wait = 12, double space = 2) =>
            new CharacterProfile("demo-sam", wait, space, "I can listen now.", "Please lower that noise.", "Please give me space.");

        CharacterTurn Turn(double distance = 3, double waited = 12, bool noisy = false, ScenarioCategory category = ScenarioCategory.UAI) =>
            new CharacterTurn(Guid.NewGuid(), Guid.NewGuid(), category, "Hello, how can I help?", distance, waited, noisy);

        [Test] public void ProfileSpecificProcessingTimeProducesExplainableWait()
        {
            var turn = Turn(waited: 5);
            var response = new OfflineCharacterBehavior().Evaluate(Profile(), turn);
            Assert.AreEqual(ResponseIntentionType.Wait, response.Type);
            Assert.AreEqual(7d, response.WaitSeconds);
            Assert.AreEqual("profile.processing", response.ReasonId);
            Assert.IsTrue(response.IsFor(turn));
        }

        [Test] public void DifferentProfilesProduceDifferentResponsesToSameObservations()
        {
            var turn = Turn(waited: 5);
            var behavior = new OfflineCharacterBehavior();
            Assert.AreEqual(ResponseIntentionType.Wait, behavior.Evaluate(Profile(), turn).Type);
            Assert.AreEqual(ResponseIntentionType.Speak, behavior.Evaluate(Profile(3), turn).Type);
        }

        [Test] public void CloseDistanceRequestsSpaceBeforeSpeaking()
        {
            var response = new OfflineCharacterBehavior().Evaluate(Profile(), Turn(distance: 1));
            Assert.AreEqual(ResponseIntentionType.RequestSpace, response.Type);
            Assert.AreEqual("Please give me space.", response.Text);
            Assert.AreEqual("profile.personal_space", response.ReasonId);
        }

        [Test] public void ExactSpaceAndWaitThresholdsAllowAuthoredReply()
        {
            var response = new OfflineCharacterBehavior().Evaluate(Profile(), Turn(distance: 2, waited: 12));
            Assert.AreEqual(ResponseIntentionType.Speak, response.Type);
            Assert.AreEqual("I can listen now.", response.Text);
        }

        [Test] public void SensoryStateSelectsOnlyProfileAuthoredDialogue()
        {
            var response = new OfflineCharacterBehavior().Evaluate(Profile(), Turn(noisy: true));
            Assert.AreEqual("Please lower that noise.", response.Text);
            Assert.AreEqual("profile.sensory", response.ReasonId);
        }

        [Test] public void CategoryIsExplicitAndNeverInferredFromOfficerText()
        {
            var turn = new CharacterTurn(Guid.NewGuid(), Guid.NewGuid(), ScenarioCategory.UAI,
                "This person might be autistic", 3, 12, false);
            new OfflineCharacterBehavior().Evaluate(Profile(), turn);
            Assert.AreEqual(ScenarioCategory.UAI, turn.Category);
            Assert.AreEqual(ScenarioCategory.KAI, Turn(category: ScenarioCategory.KAI).Category);
        }

        [Test] public void OfflineDialogueIsDeterministicAndRejectsStaleTurns()
        {
            var turn = Turn(); var profile = Profile();
            var response = new OfflineCharacterBehavior().Evaluate(profile, turn);
            var provider = new OfflineDialogueProvider();
            Assert.AreEqual("I can listen now.", provider.GenerateAsync(turn, response, CancellationToken.None).GetAwaiter().GetResult());
            Assert.Throws<ArgumentException>(() => provider.GenerateAsync(Turn(), response, CancellationToken.None).GetAwaiter().GetResult());
        }

        [Test] public void OfflineDialogueHonorsCancellation()
        {
            var turn = Turn(); var response = new OfflineCharacterBehavior().Evaluate(Profile(), turn);
            var cancellation = new CancellationToken(true);
            Assert.Throws<OperationCanceledException>(() => new OfflineDialogueProvider().GenerateAsync(turn, response, cancellation).GetAwaiter().GetResult());
        }

        [Test] public void BehaviorKeepsNoStateAcrossSessions()
        {
            var behavior = new OfflineCharacterBehavior();
            var first = Turn(distance: 1); var second = Turn();
            Assert.AreEqual(ResponseIntentionType.RequestSpace, behavior.Evaluate(Profile(), first).Type);
            var next = behavior.Evaluate(Profile(), second);
            Assert.AreEqual(ResponseIntentionType.Speak, next.Type);
            Assert.IsFalse(next.IsFor(first)); Assert.IsTrue(next.IsFor(second));
        }

        [Test] public void ResponseIntentionsRejectInvalidTypesAndUnboundedContent()
        {
            var turn = Turn();
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResponseIntention(turn, (ResponseIntentionType)999, "rule", ""));
            Assert.Throws<ArgumentException>(() => new ResponseIntention(turn, ResponseIntentionType.Speak, "rule", ""));
            Assert.Throws<ArgumentException>(() => new ResponseIntention(turn, ResponseIntentionType.Speak, "rule", new string('x', 513)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ResponseIntention(turn, ResponseIntentionType.Wait, "rule", "", double.NaN));
            Assert.AreEqual(ResponseIntentionType.Gesture, new ResponseIntention(turn, ResponseIntentionType.Gesture, "rule", "").Type);
            Assert.AreEqual(ResponseIntentionType.MoveAway, new ResponseIntention(turn, ResponseIntentionType.MoveAway, "rule", "").Type);
        }

        [Test] public void ProfilesAndObservationsRejectInvalidNumbersAndCategory()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Profile(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Profile(space: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Turn(distance: double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Turn(waited: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Turn(category: (ScenarioCategory)999));
            Assert.Throws<ArgumentException>(() => new CharacterTurn(Guid.Empty, Guid.NewGuid(), ScenarioCategory.KAI, "hello", 3, 12, false));
        }

        [Test] public void AudioInputIsDefensivelyCopiedAndValidated()
        {
            var samples = new[] { 0.25f, -0.5f };
            var audio = new PcmAudio(samples, 16000, 1);
            samples[0] = 1;
            var copy = audio.CopySamples(); Assert.AreEqual(0.25f, copy[0]);
            copy[0] = -1; Assert.AreEqual(0.25f, audio.CopySamples()[0]);
            Assert.AreEqual(16000, audio.SampleRate); Assert.AreEqual(1, audio.Channels);
            Assert.Throws<ArgumentException>(() => new PcmAudio(new[] { float.NaN }, 16000, 1));
            Assert.Throws<ArgumentException>(() => new PcmAudio(new[] { 2f }, 16000, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PcmAudio(new[] { 0f }, 0, 1));
            Assert.Throws<ArgumentException>(() => new PcmAudio(new[] { 0f }, 16000, 2));
        }
    }
}
