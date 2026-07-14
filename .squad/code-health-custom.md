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
      
      If the uncategorized approvals list is empty or says "(none)", respond with only:
      "No uncategorized items." and do not call organize_approvals.
      
      Assign a specific, descriptive feature group name to each item. You may reuse
      existing group names or invent more specific names that better reflect what the
      feature actually does (e.g. "Guided Tour", "Bug Fixes", "Developer Experience").
      
      Respond using the organize_approvals host command:
      
      HOST_COMMAND_JSON:
      [{"command":"organize_approvals","parameters":{"assignments":"[{\"sha\":\"<sha>\",\"group\":\"<group>\"},{\"sha\":\"<sha2>\",\"group\":\"<group2>\"}]"}}]
      
      Replace the placeholder SHAs and groups with the actual values from the list above.
      Include ALL uncategorized items in a single organize_approvals call.
---
