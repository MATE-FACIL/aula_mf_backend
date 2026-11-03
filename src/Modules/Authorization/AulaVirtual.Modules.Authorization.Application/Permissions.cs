namespace AulaVirtual.Modules.Authorization.Application;

// AulaVirtual catalog data, separate from the generic Authorization domain.
public static class Permissions
{
    public const string CoursesManage = "courses.manage";
    public const string CourseOfferingsManage = "course_offerings.manage";
    public const string InstructorsManage = "instructors.manage";
    public const string UsersManage = "users.manage";
    public const string EnrollmentsManage = "enrollments.manage";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { CoursesManage, CourseOfferingsManage, InstructorsManage, UsersManage, EnrollmentsManage };
}

public static class Roles
{
    public const string Administrator = "administrator";
    public const string Student = "student";
    public const string Instructor = "instructor";
    // Parent or legal guardian: signs in with email and follows linked students.
    public const string Guardian = "guardian";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { Administrator, Student, Instructor, Guardian };
}
