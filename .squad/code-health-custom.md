---
configured: true
enabled_on_idle: false
idle_timeout: 15
max_tasks_per_session: 5
safety: branch
tasks:
  - id: categorize-approvals
    enabled: true
    frequency: always
    safety: report-only
    title: Categorize Approval Items
    instructions: |
      You are categorizing uncategorized commit approval items into feature groups.
      
      The following items are currently uncategorized:
      
      {{uncategorized_approvals}}
      
      If the uncategorized approvals list is empty or says "(none)", report that there
      are no uncategorized items and do not propose or call organize_approvals.
      
      Assign a specific, descriptive feature group name to each item. You may reuse
      existing group names or invent more specific names that better reflect what the
      feature actually does (e.g. "Guided Tour", "Bug Fixes", "Developer Experience").
      
      This is a report-only task. Do not organize the items while generating the report.
      Send the proposed grouping to the Inbox and include an Apply button that executes
      the exact proposal directly through the host. Use this action shape, replacing the
      placeholders with the actual values:

      {"label":"Apply these feature groups","routeMode":"host_command","hostCommand":{"command":"organize_approvals","parameters":{"assignments":[{"sha":"<sha>","group":"<group>"},{"sha":"<sha2>","group":"<group2>"}]}}}

      Include ALL uncategorized items in one real JSON assignments array; do not encode
      the array as an escaped JSON string. The button must preserve the exact assignments
      shown in the report so applying them does not launch an agent or re-derive different
      groups. You may add review actions when useful, but never substitute them for the
      Apply button when the proposal contains assignments. Clicking Apply is the user's
      authorization for the host to validate and perform organize_approvals.
---
