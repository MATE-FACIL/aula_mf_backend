#!/bin/sh
# Aplica las migraciones EF de cada módulo contra ConnectionStrings__DefaultConnection.
set -eu

apply() {
  module="$1"; context="$2"
  echo ">> $module"
  dotnet ef database update \
    --project "src/Modules/$module/AulaVirtual.Modules.$module.Infrastructure/AulaVirtual.Modules.$module.Infrastructure.csproj" \
    --context "$context"
}

apply Users UsersDbContext
apply Auth AuthDbContext
apply Authorization AuthorizationDbContext
apply Courses CoursesDbContext
apply Enrollments EnrollmentsDbContext
apply Attendance AttendanceDbContext
apply Exams ExamsDbContext
