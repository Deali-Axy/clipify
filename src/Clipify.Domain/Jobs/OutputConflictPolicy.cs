namespace Clipify.Domain.Jobs;

public enum OutputConflictPolicy
{
    Fail = 0,
    Overwrite = 1,
    Rename = 2,
    Skip = 3,
}
