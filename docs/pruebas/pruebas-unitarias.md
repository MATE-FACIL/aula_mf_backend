# Lista de pruebas unitarias y de integración

Las 206 pruebas de `dotnet test`, por módulo y clase. Todas pasaron el 9 de noviembre de 2026.

## Administration (18)

### AdminInstructorUseCasesTests

- `Controller_RequiresInstructorsManagePermission`
- `Create_AddsPersonRoleAndLinkedInstructor`
- `Create_WithDuplicateEmail_IsAConflict_AndCreatesNoInstructor`
- `List_ResolvesNamesInOneBatch_SortedByName`

### AdminUserUseCasesTests

- `Controller_RequiresUsersManagePermission`
- `CreateGuardian_UsesEmailLogin_AndGuardianRole`
- `CreateStudent_AssignsStudentRole_AndLinksGuardian`
- `CreateStudent_WithDuplicateDocument_IsAConflict`
- `CreateStudent_WithMissingData_ReturnsFieldErrors`
- `CreateStudent_WithUnknownGuardian_CreatesNothing`
- `Detail_ListsLinkedStudentsAndGuardians`
- `Link_RequiresAGuardianAndAStudent`
- `List_ClampsPaging`
- `List_CombinesRoleAndStatusFilters_AndEnrichesEachRow`
- `List_RejectsUnknownFilters`
- `SetActive_DeactivatesOthers_ButNeverTheActor`
- `Summary_CountsNewUsersAndRoles`

## Attendance (21)

### AttendanceQueryTests

- `Model_HasUniqueClassStudent_AndStudentHistoryIndexes`
- `Recent_IsBoundedAndNewestFirst`
- `Totals_AreOneGroupedQuery`

### AttendanceRecordTests

- `Change_RecordsWhoCorrected_AndIgnoresAResubmittedSameValue`
- `StatusCodes_RoundTrip`
- `Summary_RateCountsLateAsAttended`

### AttendanceUseCaseTests

- `Administrator_CanMark_OtherAccountsCannot`
- `ConcurrentSave_IsRetriedOnce_ThenReported`
- `Correction_UpdatesTheSameRecord`
- `DraftOrUnknownClass_IsNotFound`
- `DuplicatedOrEmptyRoster_IsRejected`
- `Family_SeesOnlyTheirChildren_WithTotalsAndGroupNames`
- `FutureClass_OpensFifteenMinutesBefore`
- `GuardianWithoutChildren_GetsAnEmptyList_WithoutQueryingRecords`
- `OnlyActiveStudents_CanBeMarked`
- `StudentWhoLeft_KeepsTheirRecord_ButIsNoLongerEnrolled`
- `TeacherOfTheGroup_MarksTheWholeRoster_InOneSave`
- `UnknownStatus_IsRejected`

## Auth (43)

### AccountAdministrationTests

- `AccountsForAPage_AreLoadedWithTheirLoginsInSql`
- `Deactivation_RevokesSessions_AndIsIdempotent`
- `StatusQueries_TranslateToSql`

### AccountCreationTests

- `AdministratorCreation_UsesEmailWithoutGrantingAuthorizationData`
- `StudentCreation_CreatesUserBeforeAuthAccount`

### AuthAccountTests

- `AddIdentifier_AllowsDifferentTypesOnTheSameAccount`
- `AddIdentifier_RejectsDuplicateTypeWithinTheAccount`
- `Create_NormalizesIdentifierUsingInvariantUppercase`
- `FiveFailures_LockAccountForTenMinutes`
- `SuccessfulLogin_ResetsFailuresAndLockout`

### AuthApiTests

- `AdministratorLogin_UsesEmailAndRejectsInvalidCredentialsGenerically`
- `Cors_AllowsConfiguredOriginWithCredentials`
- `InactiveAccount_CannotLogin`
- `LoginRateLimit_AllowsTenRequestsPerMinutePerIp`
- `LoginToken_AuthorizesMeAndLogoutAll`
- `Me_RejectsInvalidBearerTokens`
- `ProtectedEndpoints_RequireBearerAuthentication`
- `RefreshCookie_IsSecureOutsideDevelopment`
- `Refresh_RotatesCookieAndReturnsAnotherValidJwt`
- `StudentLogin_ReturnsAccessTokenAndSecureCookieAttributes`

### JwtAccessTokenServiceTests

- `Generate_EmitsConfirmedHs256TokenAndClaims`
- `Settings_RejectSigningKeysShorterThan256Bits`

### LoginHandlerTests

- `Login_InactiveAccount_ReturnsGenericInvalidCredentials`
- `Login_InvalidPasswordLocksAfterFiveFailures`
- `Login_NormalizesIdentifierAndCreatesSession`
- `Login_WithEitherIdentifier_UsesTheSameAccountState`
- `Login_WithUsernameAndEmail_StartsSessionsOnTheSameAccount`

### PersistenceModelTests

- `AuthModel_UsesConfirmedTablesColumnsAndRelationships`

### PublicApiTests

- `PrivateEndpoint_StillRequiresJwt`
- `Status_IsAnonymousAndExposesOnlyPublicData`
- `Status_IsRateLimitedWithProblemDetails`
- `Status_RejectsInvalidInputAsProblemDetails`

### RefreshAndLogoutTests

- `LogoutAll_RevokesEverySession`
- `LogoutCurrent_IsIdempotent`
- `Refresh_RotatesTokenWithinSameSession`
- `ReusingRotatedToken_RevokesOnlyItsSession`

### SecurityServiceTests

- `Argon2idHasher_UsesConfirmedParametersAndUniqueSalt`
- `PasswordGenerator_ProducesRequiredTwelveCharacterPolicy`
- `RefreshTokenService_GeneratesOpaqueTokenAndStoresOnlyHash`

## Authorization (12)

### AuthorizationApiTests

- `ProtectedCourseReturns401Then403AndAuthorizationMeReportsEffectiveAccess`

### AuthorizationPersistenceTests

- `ModelUsesOwnSchemaUniqueCodesAndCompositeAssignments`

### AuthorizationTests

- `AssignmentsRequireExplicitExistingUserAndNeverInferAcademicInstructorOrLoginType`
- `ConcurrentBootstrapAttemptsOnlyAssignOneUserAndLeaveNormalRoleAssignmentAvailable`
- `DomainAndEfModelRejectDuplicateKeysAndDoNotRelateToUsersEf`
- `MultipleRolesAggregateAndRevocationsAffectNextEvaluation`
- `PolicyReadsUserIdAndCurrentRepositoryOnEveryEvaluation`
- `ProvisioningIsIdempotentAndAssignsOnlyAdministratorCapabilities`

### BootstrapConcurrencyIntegrationTests

- `ConcurrentConnectionsAgainstPostgresCreateOnlyOneInitialAdministrator`

### RoleDirectoryQueryTests

- `NoUsers_ReturnsWithoutQuerying`
- `RolesForAPage_AreResolvedInOneQuery`
- `UsersInRole_AndCounts_TranslateToSql`

## Courses (73)

### CourseAdministrationTests

- `CategoriesAreCheckedAndPublicationRequiresExistingCategory`
- `CategoryCreateGetUpdateAndDelete_PreserveIdentityAndTimestamps`
- `CategoryMissingAndReferencedDelete_AreControlled`
- `CategoryNamesAndIdentifiersAreValidated`
- `CourseResponsesIncludePublicCoverUrlInListDetailAndReplacement`
- `CourseWithCoverWithoutPublicBaseUrlDoesNotInventUrl`
- `CoverUploadReturnsServiceUnavailableWithoutStorage`
- `CoverValidationRejectsMimeEmptyAndOversizeBeforeStorage`
- `CreateUpdateGetList_KeepAcademicFieldsAndDraftState`
- `HostStartsWithStorageDisabled`
- `ListControllersResolveWithoutFileStorage`
- `ReplacementStoresThenPersistsThenDeletesPrevious`
- `SaveFailureAttemptsNewObjectCleanup`
- `UploadFailurePreservesOldCoverAndCleanupFailureKeepsNewCover`
- `ValidCoverUsesServerKeyAndPublicVisibility`

### CourseCategoryApiTests

- `CategoryCrud_UsesCoursesManageAndReturnsExpectedHttpContracts`

### CourseCategoryDeleteErrorTests

- `NonPostgresErrorsAreNotTranslated`
- `OnlyCourseCategoryReferenceErrorsBecomeConflicts`

### CourseContentQueryTests

- `Model_HasTheIndexesTheQueriesUse`
- `NextPosition_IsAMaxQuery_ServedByTheUnitIndex`
- `PublishedContent_StartsWithTheCourse`

### CourseContentUseCaseTests

- `InvalidContent_IsReportedWithAReadableMessage`
- `Lessons_AreNumberedAcrossUnits_InUnitThenLessonOrder`
- `MovingALesson_PutsItAtTheEndOfTheOtherUnit`
- `OtherCoursesUnitsAndLessons_AreNotFound`
- `Reorder_MustListEveryItemOnce`
- `UnitWithLessons_CannotBeDeleted_ButAnEmptyOneCan`
- `UnitsArePerCourseCapped`

### CourseOfferingAuthorizationTests

- `AdministrativeControllers_RequireTheirPermission`

### CourseOfferingCommercialTests

- `CommercialUseCasesCheckInstructorExistenceAndReturnPricePreview`
- `CurrencyCodeRequiresStableUppercaseFormat`
- `EditionSupportsManyInstructorsAndInstructorCanBeReused`
- `FixedAmountUsesEditionCurrencyAndClampsPriceAtZero`
- `InvalidPromotionIsRejected`
- `PercentagePromotionWinsOverGlobalAndNeverAccumulates`
- `PublicationRequiresAnInstructor`

### CourseOfferingTests

- `Create_RejectsInvalidCalendar`
- `Create_RequiresCourseAndStoresFinalSessionsAsDraft`
- `DraftCanBeEdited_ButPublishedCalendarCannotBeReplaced`
- `InvalidDraftEdit_DoesNotPartiallyChangeSessions`

### CourseOfferingUseCaseTests

- `CreateGetListUpdateAndPublish_StayWithinCourse`

### CourseSessionDurationTests

- `Duration_IsOptional_AndBounded`
- `Validator_ChecksTheRange`

### CourseTests

- `Course_RequiresCategoryBeforePublication`
- `NewCourse_IsInternalByDefault`

### InstructorTests

- `InstructorExistsWithoutUserAndKeepsItsIdentityWhenLinked`
- `LinkUserChecksExistenceAndRejectsDuplicateInstructorIdentity`

### LessonDomainTests

- `Create_TrimsTexts_AndKeepsMaterialOrder`
- `Materials_AreCapped`
- `Update_ReplacesMaterials_AndABadMaterialChangesNothing`
- `Video_MustBeHttps`

### OfferingDirectoryQueryTests

- `GetMany_UsesOneQuery`
- `Get_FiltersBeforeProjecting`
- `List_OrdersByCourseAndName`

### OfferingScheduleQueryTests

- `NoEditions_ReturnsWithoutQuerying`
- `Sessions_JoinEditionCourseAndCategory_InOneStatement`

### PersistenceModelTests

- `CoursesModel_UsesOwnSchemaAndPublicationGuard`
- `OfferingModel_MapsOnlyCoursesOwnedRelationships`

### PublicCourseCatalogTests

- `Catalog_UsesExistingPublicRateLimitAndProblemDetails`
- `EfQuery_TranslatesPublishedFilterAndProjectsOnlyCatalogColumns`
- `EmptyCatalog_ReturnsAnonymous200Array`
- `SpecificPromotions_UseCurrentWindowAndExistingPriceRules`
- `VisibilityAndPublicContract_KeepOnlyPublishedCoursesAndOfferings`

## Enrollments (26)

### EnrollmentDomainTests

- `Activation_RecordsWhoAndWhen_AndOnlyFromPending`
- `RejectsEmptyIdsAndLongNotes`
- `Starts_Pending_AndRecordsWhoRegistered`
- `Withdrawal_FreesTheSeat_AndCannotRepeat`

### EnrollmentQueryTests

- `Search_FiltersAndPagesInSql`
- `SeatCount_IgnoresWithdrawn_AndGroupsByEdition`

### EnrollmentUseCaseTests

- `ActivateAndWithdraw_ValidateTheCurrentState`
- `Controller_RequiresEnrollmentsManagePermission`
- `Enroll_CreatesPending_WithNamesForTheList`
- `Enroll_RejectsDraftEditions_WithoutTakingASeat`
- `Enroll_RejectsNonStudents_UnknownEditions_AndDuplicates`
- `Enroll_RespectsCapacity_AndWithdrawalFreesTheSeat`
- `List_FiltersByStatus_AndRejectsUnknownStatus`

### FamilyTests

- `AccountWithoutChildren_GetsAnEmptyList_WithoutFurtherQueries`
- `EachChild_HasTheirEnrollments_AndClassesOnlyFromActiveOnes`
- `Guardian_SeesOnlyTheirOwnChildren`
- `LiveSeats_TranslateToSql`
- `NoQueryRunsPerChild`
- `Upcoming_IsLimitedPerChild`

### StudentLessonsTests

- `ActiveCourse_ShowsItsPublishedLessons`
- `CourseWithoutActiveEnrollment_IsNotShown_AndContentIsNeverRead`
- `DraftGroups_AreLeftOut`

### StudentScheduleTests

- `ActiveOfferings_AndSessions_TranslateToSql`
- `OnlyActiveEnrollments_ShowClasses_WithTeacherNames`
- `RejectsInvertedOrTooLongRanges`
- `WithoutActiveEnrollments_DoesNotQueryCourses`

## Users (9)

### BackOfficeQueryTests

- `EmptyRestriction_ReturnsWithoutQuerying`
- `Get_ProjectsLinkCountAsCorrelatedSubquery`
- `GuardianLinkModel_UsesCompositeKeyAndStudentIndex`
- `GuardianLink_RejectsSelfLinks`
- `Search_FiltersEveryTermInSql_IgnoringAccents`
- `Unlink_DeletesInOneStatement`

### CreateUserTests

- `CreateUser_RejectsDuplicateDocument`
- `CreateUser_TrimsValuesAndReturnsUserId`

### PersistenceModelTests

- `UsersModel_UsesConfirmedSchemaTableAndFilteredUniqueIndex`

## SharedKernel.Storage (4)

### StorageTests

- `Configuration_Rejects_Invalid_And_Selects_Separate_Buckets`
- `Disabled_Storage_Does_Not_Register_Adapter`
- `Keys_Are_Server_Shaped_And_Reject_Traversal`
- `Signed_Grants_Are_Scoped_And_Contain_No_Secret_Key`
