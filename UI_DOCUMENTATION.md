# NU Lipa Mobile App - UI Documentation

## Project Overview
This is a .NET MAUI mobile application for the **NU Lipa Student Organization Governance Support System**. The mobile app focuses on:
- Automated approval process notifications
- Controlled collaborative editing with role-based restrictions
- Revision history tracking
- Multi-stage approval workflow

## Current Status: UI Only
✅ **Complete UI implementation with dummy data**
❌ No backend integration yet
❌ No database connection yet
❌ No authentication logic yet

---

## Application Architecture

### Folder Structure
```
docusystem/
├── Pages/
│   ├── Login/
│   │   ├── LoginPage.xaml
│   │   └── LoginPage.xaml.cs
│   ├── Dashboard/
│   │   ├── DashboardPage.xaml
│   │   └── DashboardPage.xaml.cs
│   ├── Notifications/
│   │   ├── NotificationsPage.xaml
│   │   └── NotificationsPage.xaml.cs
│   ├── Approvals/
│   │   ├── PendingApprovalsPage.xaml
│   │   ├── PendingApprovalsPage.xaml.cs
│   │   ├── ProposalDetailsPage.xaml
│   │   ├── ProposalDetailsPage.xaml.cs
│   │   ├── EditProposalPage.xaml
│   │   └── EditProposalPage.xaml.cs
│   └── Revisions/
│       ├── RevisionHistoryPage.xaml
│       └── RevisionHistoryPage.xaml.cs
├── Models/
│   └── DummyData.cs          # Data models and dummy data provider
├── App.xaml
├── AppShell.xaml             # Navigation configuration
└── MauiProgram.cs            # App setup and dependency injection
```

---

## Pages Overview

### 1. **LoginPage**
**Route:** `login`

**Purpose:** User authentication entry point

**Features:**
- Email and password input fields
- Password show/hide toggle
- Form validation with error messages
- Loading state indicator
- "Forgot Password?" link
- "Create Account" link
- Responsive card-style design

**Navigation:** Login → Dashboard

**Integration Notes:**
- TODO: Connect to Laravel API endpoint `/api/login`
- TODO: Add authentication token storage
- TODO: Implement actual validation

---

### 2. **DashboardPage**
**Route:** `dashboard`

**Purpose:** Main hub showing approval overview and recent activity

**Features:**
- User welcome message with role
- Summary cards showing:
  - Pending Approvals (3)
  - Returned Proposals (1)
  - Approved Proposals (12)
  - New Notifications (2)
- Recent activity feed with quick-access buttons
- Color-coded status badges

**Navigation:**
- Dashboard → Pending Approvals
- Dashboard → Notifications
- Dashboard → Returned Proposals (filtered view)
- Dashboard → Approved Proposals (filtered view)

**Integration Notes:**
- TODO: Load user data from API
- TODO: Fetch real approval counts from database
- TODO: Load recent activity from server
- Data source: `DummyDataProvider.GetDummyRecentActivity()`

---

### 3. **NotificationsPage**
**Route:** `notifications`

**Purpose:** Display all notifications with filtering capabilities

**Features:**
- Filterable notification list
- Filter options:
  - All (default)
  - Unread
  - Approval Updates
  - Revision Updates
- Active filter highlighting
- Notification cards with:
  - Title and message
  - Timestamp (relative format)
  - Read/Unread status badge
  - Type indicator

**Navigation:** Dashboard → Notifications

**Integration Notes:**
- TODO: Connect to notification API
- TODO: Implement real-time notification updates
- TODO: Add notification marking as read
- Data source: `DummyDataProvider.GetDummyNotifications()`

---

### 4. **PendingApprovalsPage**
**Route:** `pendingapprovals`

**Purpose:** List proposals awaiting current user's approval

**Features:**
- Searchable proposal list (by title or organization)
- Status filter:
  - All (default)
  - Pending
  - Returned
- Proposal cards displaying:
  - Title
  - Organization name
  - Current approval stage
  - Status (color-coded)
  - Submission date
  - "View Details" button
- Responsive grid layout

**Navigation:** Dashboard → Pending Approvals → Proposal Details

**Integration Notes:**
- TODO: Fetch proposals assigned to current user
- TODO: Implement search with API
- TODO: Add pagination for large lists
- TODO: Filter by user's role (get only applicable proposals)
- Data source: `DummyDataProvider.GetDummyProposals()`

---

### 5. **ProposalDetailsPage**
**Route:** `proposaldetails`

**Purpose:** Display complete proposal information and approval actions

**Features:**
- Proposal header with:
  - Title
  - Organization
  - Status badge
  - Submitted by
- Proposal information section:
  - Date
  - Venue
  - Budget
  - Current stage
- Full description and objectives
- Remarks input area (for approval/rejection notes)
- Approval progress timeline showing:
  - All signatories
  - Current approval status
  - Approval dates and remarks
- Action buttons:
  - ✓ Approve Proposal
  - ↶ Return for Revision
  - ✎ Edit Proposal (restricted)
  - ⟲ View Revision History

**Key Feature - Role-Based Editing:**
- Edit button only enabled for:
  - RSO President (always)
  - Current reviewer
- Visual warning banner explains restrictions

**Navigation:**
- Pending Approvals → Proposal Details
- Proposal Details → Edit Proposal
- Proposal Details → Revision History

**Integration Notes:**
- TODO: Fetch full proposal data from API
- TODO: Get current approval step
- TODO: Check user permissions for editing
- TODO: Implement approval/rejection actions
- TODO: Send remarks to API
- Data source: `DummyDataProvider.GetDummyProposals()` and `DummyDataProvider.GetDummyApprovalSteps()`

---

### 6. **EditProposalPage**
**Route:** `editproposal`

**Purpose:** Edit proposal details with restrictions

**Features:**
- Warning banners showing:
  - "Editable only at current approval stage"
  - "Only RSO President and current reviewer can edit"
- Editable form fields:
  - Title
  - Activity Date
  - Venue
  - Budget
  - Description (Editor control)
  - Objectives (Editor control)
- Locked sections for future approval stages (read-only visual)
- Save Changes and Cancel buttons
- Validation for required fields

**Key Restrictions:**
- Only RSO President and current reviewer can edit
- Fields can only be edited during the current approval stage
- Future approval stages show as locked

**Navigation:** Proposal Details → Edit Proposal → Back to Details

**Integration Notes:**
- TODO: Check user permission before allowing edits
- TODO: Validate that proposal is at editable stage
- TODO: Send updated fields to API
- TODO: Track which fields were changed for revision history
- TODO: Implement field-level validation
- Data source: `DummyDataProvider.GetDummyProposals()`

---

### 7. **RevisionHistoryPage**
**Route:** `revisionhistory`

**Purpose:** Track all changes made to a proposal

**Features:**
- Reverse-chronological timeline of changes
- Each revision entry shows:
  - Editor name and role
  - Timestamp (relative format)
  - Field that was changed
  - Old value (in red highlight)
  - New value (in green highlight)
  - Visual change indicator arrow
- Color-coded cards for visual clarity
- Complete audit trail

**Navigation:** Proposal Details → Revision History

**Integration Notes:**
- TODO: Fetch revision history from API
- TODO: Implement full audit logging on backend
- TODO: Track user, timestamp, and field changes
- TODO: Store old and new values
- Data source: `DummyDataProvider.GetDummyRevisionHistory()`

---

## Navigation Flow

```
LoginPage
    ↓
DashboardPage
    ├→ Notifications Page
    ├→ Pending Approvals
    │    ├→ Proposal Details
    │    │    ├→ Edit Proposal
    │    │    └→ Revision History
    │    └→ (back to Dashboard)
    └→ (Other filtered views)
```

---

## Data Models

### Proposal
```csharp
public class Proposal
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string OrganizationName { get; set; }
    public string SubmittedBy { get; set; }
    public string CurrentStage { get; set; }
    public string Status { get; set; }
    public DateTime SubmittedDate { get; set; }
    public DateTime ActivityDate { get; set; }
    public string Venue { get; set; }
    public decimal Budget { get; set; }
    public string Description { get; set; }
    public string Objectives { get; set; }
}
```

### ApprovalStep
```csharp
public class ApprovalStep
{
    public int Order { get; set; }
    public string Role { get; set; }
    public string Status { get; set; } // Pending, Approved, Returned, Locked
    public string SignatoryName { get; set; }
    public DateTime? ApprovedDate { get; set; }
    public string Remarks { get; set; }
}
```

### Notification
```csharp
public class Notification
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public DateTime DateTime { get; set; }
    public string Status { get; set; } // Unread, Read
    public string Type { get; set; } // ApprovalUpdate, RevisionUpdate
}
```

### RevisionEntry
```csharp
public class RevisionEntry
{
    public int Id { get; set; }
    public string EditedBy { get; set; }
    public string Role { get; set; }
    public string FieldChanged { get; set; }
    public string OldValue { get; set; }
    public string NewValue { get; set; }
    public DateTime Timestamp { get; set; }
}
```

---

## Design System

### Color Palette
- **Primary Blue:** `#0066CC` (buttons, active states)
- **Navy (Dark):** `#001F3F` (text, headers)
- **Academic Blue:** `#4B7BA7` (secondary text)
- **Success Green:** `#28A745` (approved status)
- **Error Red:** `#E63946` (returned/error status)
- **Warning Orange:** `#FFA500` (pending/warning)
- **Light Blue:** `#E2EBF0` (borders, inactive buttons)
- **Light Gray:** `#F8FAFC` (page background)
- **White:** `#FFFFFF` (cards, content)

### Typography
- **Headers:** Bold, 24-18px, Navy
- **Subheaders:** Bold, 16-14px, Navy
- **Body:** Regular, 12-14px, Academic Blue
- **Labels:** Bold, 11-13px, Navy
- **Helper Text:** 10-11px, Gray

### Components
- **Cards:** Rounded corners (10-12px), soft shadows, white background
- **Buttons:** Rounded corners (6-8px), padding, responsive touch targets
- **Input Fields:** Rounded, bordered (1px), navy border
- **Dividers:** 1px line, light gray
- **Status Badges:** Small frames with background color

---

## Dummy Data

All dummy data is provided by `DummyDataProvider` static class in `Models/DummyData.cs`:

- `GetDummyProposals()` - 3 sample proposals
- `GetDummyNotifications()` - 4 sample notifications
- `GetDummyApprovalSteps()` - 5-step approval workflow
- `GetDummyRevisionHistory()` - 3 sample revisions
- `GetDummyRecentActivity()` - 3 activity entries
- `GetDummySummaryStats()` - Dashboard counts

---

## Backend Integration Checklist

### Authentication
- [ ] Implement LoginPage API call
- [ ] Store authentication token securely
- [ ] Add automatic token refresh
- [ ] Handle OAuth or alternative auth methods

### API Endpoints Needed
```
POST   /api/login
GET    /api/dashboard/summary
GET    /api/proposals
GET    /api/proposals/{id}
PUT    /api/proposals/{id}
POST   /api/proposals/{id}/approve
POST   /api/proposals/{id}/return
GET    /api/proposals/{id}/revisions
GET    /api/notifications
PUT    /api/notifications/{id}/read
```

### Database Models Required
- Users (with roles)
- Proposals
- ApprovalWorkflow/Steps
- Notifications
- RevisionHistory/AuditLog
- ProposalRemarks

---

## Code Style & Beginner-Friendly Notes

### Naming Conventions
- **Pages:** `*Page.xaml` and `*Page.xaml.cs`
- **Controls:** `x:Name` uses PascalCase with context suffix (e.g., `EmailEntry`, `ApproveBtn`)
- **Methods:** PascalCase, event handlers start with `On` (e.g., `OnApproveClicked`)
- **Variables:** camelCase for local variables

### Code Organization
- **XAML:** UI layout first, with comments separating major sections
- **Code-Behind:** Constructor, then public methods, then private helper methods
- **Comments:** Extensive XML documentation (`///`) on public methods for beginner understanding

### Key Comments
- Every file has a summary header explaining its purpose
- Every public method has documentation
- Inline comments explain non-obvious logic
- TODO comments mark places for backend integration

---

## Testing with Dummy Data

All pages load sample data by default:
1. Run the app
2. Skip login (navigate to dashboard manually)
3. Explore all pages
4. Test filtering and search
5. Click all buttons (show placeholder alerts)
6. Test form inputs and validation

---

## Future Enhancements

1. **Real API Integration**
   - Replace dummy data with API calls
   - Implement proper error handling
   - Add loading spinners and offline mode

2. **Form Validation**
   - Enhance client-side validation
   - Add date picker for date fields
   - Add currency input for budget

3. **Real-Time Updates**
   - WebSocket for notifications
   - SignalR for approval updates
   - Automatic refresh on changes

4. **Advanced Features**
   - Bulk approval actions
   - Advanced filtering options
   - Sorting (by date, status, organization)
   - Attachment uploads
   - Comment threads
   - Email notifications

5. **Performance**
   - Implement pagination
   - Lazy loading for large lists
   - Caching strategies
   - Image optimization

---

## Support

For questions or issues:
1. Check the TODO comments in the code
2. Review this documentation
3. Check the Models/DummyData.cs for data structure
4. Review XAML structure for UI understanding

---

**Last Updated:** March 25, 2026
**Status:** UI Complete, Ready for Backend Integration
