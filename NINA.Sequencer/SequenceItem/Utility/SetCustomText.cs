using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Equipment.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NINA.Sequencer.SequenceItem.Utility {
    [ExportMetadata("Name", "Lbl_SequenceItem_Utility_SetCustomText_Name")]
    [ExportMetadata("Description", "Lbl_SequenceItem_Utility_SetCustomText_Description")]
    [ExportMetadata("Icon", "Pen_NoFill_SVG")]
    [ExportMetadata("Category", "Lbl_SequenceCategory_Utility")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class SetCustomText : SequenceItem {

        [ImportingConstructor]
        public SetCustomText(IOptionsVM options, IImageSaveMediator imageSaveMediator, IImagingMediator imagingMediator, ICameraMediator cameraMediator) {
            this.imageSaveMediator = imageSaveMediator;
            this.imagingMediator = imagingMediator;

            CustomPatterns.ForEach(p => options.AddImagePattern(p));
        }

        public override object Clone() {
            return new SetCustomText(this) {
                Text = Text,
                PatternKey = PatternKey,
            };
        }

        private SetCustomText(SetCustomText cloneMe) {
            // grab on the mediators from the original object
            imageSaveMediator = cloneMe.imageSaveMediator;
            imagingMediator = cloneMe.imagingMediator;

            imagePatternToBeAdded = cloneMe.imagePatternToBeAdded;

            CopyMetaData(cloneMe);
        }

        private IImageSaveMediator imageSaveMediator;
        private IImagingMediator imagingMediator;

        private static List<ImagePattern> customPatterns = new List<ImagePattern> {
            new(Loc.Instance["LblCustomText1Key"], Loc.Instance["LblCustomText1"], Loc.Instance["LblCustomTexts"]),
            new(Loc.Instance["LblCustomText2Key"], Loc.Instance["LblCustomText2"], Loc.Instance["LblCustomTexts"]),
            new(Loc.Instance["LblCustomText3Key"], Loc.Instance["LblCustomText3"], Loc.Instance["LblCustomTexts"]),
            new(Loc.Instance["LblCustomText4Key"], Loc.Instance["LblCustomText4"], Loc.Instance["LblCustomTexts"]),
            new(Loc.Instance["LblCustomText5Key"], Loc.Instance["LblCustomText5"], Loc.Instance["LblCustomTexts"]),
        };

        public static List<ImagePattern> CustomPatterns { get { return customPatterns; } }

        [JsonProperty]
        public string Text { get; set; }

        [JsonProperty]
        public string PatternKey { get; set; }

        private ImagePattern imagePatternToBeAdded = null;
        private bool executePending = false;

        public override void Initialize() {
            this.imageSaveMediator.BeforeFinalizeImageSaved += ImageSaveMediator_BeforeFinalizeImageSaved;
            this.imageSaveMediator.ImageSaved += ImageSaveMediator_ImageSaved;
            this.imageSaveMediator.BeforeImageSaved += ImageSaveMediator_BeforeImageSaved;

            base.Initialize();
        }

        private Task waitForPrepTask = null;
        private async Task ImageSaveMediator_BeforeImageSaved(object sender, BeforeImageSavedEventArgs e) {
            // Pick up the imagePrepare task so the we can wait for it to finish before we change the custom patterns

            await WaitForPrepareTask(e.ImagePrepareTask);
        }

        private Task ImageSaveMediator_BeforeFinalizeImageSaved(object sender, BeforeFinalizeImageSavedEventArgs e) {
            // This is where we inject the custom patterns.  

            if (!e.Patterns.Any(p => p.Key == PatternKey))
                CustomPatterns.Where(p => p.Key == PatternKey).ToList().ForEach(p => e.AddImagePattern(p));

            return Task.CompletedTask;
        }

        private void ImageSaveMediator_ImageSaved(object sender, ImageSavedEventArgs e) {

            if (executePending) {   // the instruction was executed while an image was being processed.  This event has fired for that earlier image
                                    // now that the image has been saved we can set the text of the pattern and it will be injected the next 
                                    // time an image is Finalized
                SetText();
            }
        }

        private async Task execTask() {
            // give any events from preceeding image captures chance to fire
            await Task.Delay(500);

            if (waitingForImagePrep) {
                executePending = true;
                return;
            }

            SetText();

            return;
        }
        public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            _ = execTask();
            return Task.CompletedTask;
        }

        private bool waitingForImagePrep = false;
        private async Task WaitForPrepareTask(Task imagePrepareTask) {
            if (imagePrepareTask == null) {
                return;
            }
            if (imagePrepareTask.IsCompleted) {
                return;
            }
            if (imagePrepareTask.IsCanceled) {
                return;
            }

            waitingForImagePrep = true;
            await imagePrepareTask;
            waitingForImagePrep = false;
            if (executePending) {
                SetText();
            }
        }

        private void SetText() {
            CustomPatterns.Where(p => p.Key == PatternKey)
                .FirstOrDefault().Value = Text;
            executePending = false;
        }

        public override async void Teardown() {
            // allow for a BeforeImageSaved to be fired before tearing down
            await Task.Delay(750);

            // BeforeImageSaved has been called so wait for the image to be prepared
            if (waitingForImagePrep) {
                await waitForPrepTask;
            }

            // last chance
            if (executePending)
                SetText();

            // wait 10 secs before removing the hooks that we need for any outstanding file save operations
            await Task.Delay(10000);
            imageSaveMediator.BeforeFinalizeImageSaved -= ImageSaveMediator_BeforeFinalizeImageSaved;
            imageSaveMediator.ImageSaved -= ImageSaveMediator_ImageSaved;
            imageSaveMediator.BeforeImageSaved -= ImageSaveMediator_BeforeImageSaved;

            return;
        }

    }
}
