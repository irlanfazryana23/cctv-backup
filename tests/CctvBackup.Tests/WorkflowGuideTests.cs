using CctvBackup.Core.Workflow;

namespace CctvBackup.Tests;

public class WorkflowGuideTests
{
    // A user who has everything chosen and is about to search.
    private static readonly GuideState Ready = new(Branches: 3, TickedBranches: 1, FolderChosen: true);

    private static GuideStep Step(GuideState s) => WorkflowGuide.Decide(s);

    [Fact]
    public void Missing_driver_comes_first()
        => Assert.Equal(GuideStep.DriverMissing, Step(Ready with { DriverMissing = true }));

    [Fact]
    public void No_branches_means_add_one()
        => Assert.Equal(GuideStep.NoBranches, Step(new GuideState()));

    [Fact]
    public void Branches_but_none_ticked_means_tick_one()
        => Assert.Equal(GuideStep.TickBranch, Step(new GuideState(Branches: 5, FolderChosen: true)));

    [Fact]
    public void Ticked_branch_without_camera_info_means_test_the_connection()
        => Assert.Equal(GuideStep.CamerasUnknown, Step(Ready with { TickedWithoutCameraInfo = 1 }));

    [Fact]
    public void All_ticked_branches_without_a_chosen_camera_means_tick_a_camera()
        => Assert.Equal(GuideStep.TickCamera, Step(Ready with { TickedBranches = 2, TickedWithNoCameraChosen = 2 }));

    [Fact]
    public void One_branch_with_cameras_is_enough_to_go_on()
        => Assert.Equal(GuideStep.Find, Step(Ready with { TickedBranches = 2, TickedWithNoCameraChosen = 1 }));

    [Fact]
    public void Missing_folder_comes_before_searching()
        => Assert.Equal(GuideStep.ChooseFolder, Step(Ready with { FolderChosen = false }));

    [Fact]
    public void Everything_chosen_and_no_list_means_find()
        => Assert.Equal(GuideStep.Find, Step(Ready));

    [Fact]
    public void While_searching_and_downloading_that_is_what_is_shown()
    {
        Assert.Equal(GuideStep.Searching, Step(Ready with { Searching = true }));
        Assert.Equal(GuideStep.Downloading, Step(Ready with { Downloading = true, ListExists = true, Ticked = 4 }));
    }

    [Fact]
    public void Changed_choices_make_the_list_outdated()
        => Assert.Equal(GuideStep.ListOutdated, Step(Ready with { ListExists = true, ListOutdated = true, Recordings = 10, Ticked = 10 }));

    [Fact]
    public void An_empty_result_says_nothing_found()
        => Assert.Equal(GuideStep.NothingFound, Step(Ready with { ListExists = true }));

    [Fact]
    public void An_empty_result_with_search_problems_says_the_search_failed()
        => Assert.Equal(GuideStep.SearchFailed, Step(Ready with { ListExists = true, SearchProblems = 2 }));

    [Fact]
    public void A_result_that_is_all_on_the_disk_already_says_so()
        => Assert.Equal(GuideStep.AllAlreadySaved, Step(Ready with { ListExists = true, AlreadySaved = 6 }));

    [Fact]
    public void Files_listed_but_none_ticked_means_tick_recordings()
        => Assert.Equal(GuideStep.TickRecordings, Step(Ready with { ListExists = true, Recordings = 8 }));

    [Fact]
    public void Ticked_files_mean_ready_to_download()
        => Assert.Equal(GuideStep.ReadyToDownload, Step(Ready with { ListExists = true, Recordings = 8, Ticked = 8 }));

    [Fact]
    public void After_a_run_with_nothing_left_it_is_finished()
        => Assert.Equal(GuideStep.Finished, Step(Ready with { ListExists = true, Recordings = 8, Done = 8 }));

    [Fact]
    public void Failed_files_still_ticked_after_a_run_offer_the_retry()
        => Assert.Equal(GuideStep.ReadyToDownload, Step(Ready with { ListExists = true, Recordings = 8, Done = 6, Ticked = 2 }));
}
