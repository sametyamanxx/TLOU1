using System.Runtime.ExceptionServices;
using TLOUTrainer.Core;
using TLOUTrainer.UI;
using Xunit;

namespace Trainer.Tests;

public sealed class FeatureLayoutTests
{
    [Fact]
    public void MainWindowOnlyHasAmmoF1AndHealthF2()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // No Show/message loop: this test never attaches to the game.
                using var form = new MainForm();
                var rows = form.Controls.OfType<FeatureRow>().OrderBy(r => r.Top).ToArray();
                Assert.Equal(2, rows.Length);
                Assert.Equal("Sınırsız Mühimmat", rows[0].Title);
                Assert.Equal("F1", rows[0].Hotkey);
                Assert.Equal("Sınırsız Sağlık", rows[1].Title);
                Assert.Equal("F2", rows[1].Hotkey);
                Assert.Equal(6, form.Controls.Count);
                Assert.Empty(form.Controls.OfType<ThemeButton>());
                Assert.Equal(2, form.Controls.OfType<ChromeButton>().Count());
            }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Arayüz testi zaman aşımına uğradı.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void RemovedWeaponActionAndCallStubAreAbsent()
    {
        Assert.Null(typeof(TrainerSession).GetMethod("GiveAll"));
        Assert.Null(typeof(Analyzer).GetMethod("FindGiveAllAction"));
        Assert.Null(typeof(AnalysisResult).GetProperty("GiveAllRva"));
        Assert.Null(typeof(TrainerSession).Assembly.GetType("TLOUTrainer.Core.RemoteCall"));
    }
}
