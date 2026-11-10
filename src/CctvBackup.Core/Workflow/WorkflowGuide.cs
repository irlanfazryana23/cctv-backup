namespace CctvBackup.Core.Workflow;

/// <summary>Where the user is in the backup, i.e. what the screen should tell them to do next.</summary>
public enum GuideStep
{
    DriverMissing,
    NoBranches,
    TickBranch,
    CamerasUnknown,
    TickCamera,
    ChooseFolder,
    Find,
    Searching,
    ListOutdated,
    NothingFound,
    SearchFailed,
    AllAlreadySaved,
    TickRecordings,
    ReadyToDownload,
    Downloading,
    Finished,
}

/// <summary>What is on the screen right now (only the facts that decide the next step).</summary>
/// <param name="TickedBranches">Branches ticked in the list.</param>
/// <param name="TickedWithoutCameraInfo">Ticked branches whose cameras are not known yet (no camera list at all).</param>
/// <param name="TickedWithNoCameraChosen">Ticked branches that have cameras but none of them chosen.</param>
/// <param name="ListExists">A search has been done and its result is on screen.</param>
/// <param name="ListOutdated">The choices changed after that search.</param>
/// <param name="Recordings">Files in the list that are not already saved.</param>
/// <param name="Ticked">Files ticked for download and still to do.</param>
/// <param name="Done">Files saved in the run that just finished.</param>
/// <param name="AlreadySaved">Files in the list that were already on the disk.</param>
/// <param name="SearchProblems">Branches that could not be searched in the last search.</param>
public sealed record GuideState(
    bool DriverMissing = false,
    int Branches = 0,
    int TickedBranches = 0,
    int TickedWithoutCameraInfo = 0,
    int TickedWithNoCameraChosen = 0,
    bool FolderChosen = false,
    bool Searching = false,
    bool Downloading = false,
    bool ListExists = false,
    bool ListOutdated = false,
    int Recordings = 0,
    int Ticked = 0,
    int Done = 0,
    int AlreadySaved = 0,
    int SearchProblems = 0);

public static class WorkflowGuide
{
    /// <summary>The one thing the user most likely needs to do now. The first rule that applies wins.</summary>
    public static GuideStep Decide(GuideState s)
    {
        if (s.DriverMissing) return GuideStep.DriverMissing;
        if (s.Downloading) return GuideStep.Downloading;
        if (s.Searching) return GuideStep.Searching;
        if (s.Branches == 0) return GuideStep.NoBranches;
        if (s.TickedBranches == 0) return GuideStep.TickBranch;
        if (s.TickedWithoutCameraInfo > 0) return GuideStep.CamerasUnknown;
        if (s.TickedWithNoCameraChosen >= s.TickedBranches) return GuideStep.TickCamera;
        if (!s.FolderChosen) return GuideStep.ChooseFolder;

        if (s.ListExists && s.ListOutdated) return GuideStep.ListOutdated;
        if (s.ListExists)
        {
            if (s.Ticked > 0) return GuideStep.ReadyToDownload;
            if (s.Done > 0) return GuideStep.Finished;
            if (s.Recordings == 0 && s.AlreadySaved == 0) return s.SearchProblems > 0 ? GuideStep.SearchFailed : GuideStep.NothingFound;
            if (s.Recordings == 0) return GuideStep.AllAlreadySaved;
            return GuideStep.TickRecordings;
        }
        return GuideStep.Find;
    }
}
