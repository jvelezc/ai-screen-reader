# Deliver the Privensic investigation mission

Privensic should take a website and a business objective, attempt that objective through its own screen reader, and return a determination that can be traced to the evidence. The report explains what happened in the task, what accessibility barrier the evidence supports, whether NVDA independently reproduced it, and which legal elements the documented facts support. Each answer can have its own uncertainty.

Today the production workflow reviews a public page and tests supported service links through the proprietary reader. The GentleBirth pricing task provides end-to-end production evidence for that scope. Current target discovery still uses DOM information, and the public keyboard worker repeats a sequential route. Recorded search and checkout fixtures, historical NVDA comparisons, and the review’s evidence-readiness decision are foundations for the broader mission. [Production task evidence](validation/public-task-reader-v1.md) · [Current release acceptance](validation/reader-navigation-acceptance-v2.md) · [Review assessment contract](validation/public-review-assessment-v1.md)

## Guide the user through the investigation

1. **Request.** Show the website, what Privensic can attempt, and what counts as completion. Today the user enters a website and Privensic identifies supported public link tasks. When journey selection ships, let the user specify the business objective and confirm the supported scope and permitted actions.
2. **Review and attempt.** Distinguish the initial page review from actual reader interaction. Show which tasks are ready, running, completed, stopped, or unavailable. Reopening a saved investigation returns to the same request and attempts.
3. **Review the evidence.** Lead with the observed task outcome and where progress changed. Link to the recorded reader interaction, replay, and originals. Say what evidence is missing and give the next action available for that state.
4. **Read the determination.** Put the tested scope beside the result. Today the result can describe a documented barrier in a tested task, completion without an observed blocking barrier, or insufficient evidence. As independent validation and legal analysis ship, show their results and limitations separately in the same report. Keep the evidence, reproduction steps, and applicable sources within reach from each conclusion.

## Implementation sequence and acceptance

### 1. Make public task navigation reader-driven from discovery to completion

Replace live target selection in the public task worker with a planner whose inputs are the declared objective, reader observations, command results, and attempt history. Offer typed reader commands for browse and focus navigation, including landmarks, headings, links, focus traversal, and activation. Keep diagnostic DOM access, screenshots, and pointer comparison behind separate capabilities. Policy and outcome verification may use trusted browser facts internally. The planner sees only the result the user could observe through the reader. Record which route it tried and why it continued, switched, or stopped.

Accept on real Chromium running owned working, blocked, and uncertain fixtures. A visually present target missing from the accessibility surface and an off-path DOM decoy must not guide selection. A supported target beyond 70 controls remains reachable. At least one fixture must succeed through an alternate reader route after the primary route cycles. A repeated route, unavailable observation, lost authority, or unconfirmed input must retain its specific stop reason. Only attributable, reproduced interface behavior can support a documented barrier. Run reader and browser regression checks through the existing core entry points.

### 2. Preserve stopped attempts and make evidence independently reviewable

Start this work alongside stage 1. Attribute requested cancellation, lease loss, and native or capture failures. Retain permissible partial originals and ordered input, focus, observation, and state changes under the original attempt identity. Publish the stop reason and missing records even when a report remains incomplete. Exercise authority loss before and after input, crash during finalization or upload, and storage failure without replaying uncertain input. Prove native retirement separately from API status. Continue the existing GAP-07 work and record fault reproductions in the issue catalogue.

Use a measured shared timeline to associate supported events with media intervals. Export a reproducible package with the objective, route, steps, reader transcript, relevant snapshots, recordings, timestamps, environment versions, source identities, and original file hashes. Label derived frames with source video and timestamp. Accept when an independent verifier can detect changed or missing files and replay the supported event, and the original account can reopen and export the same evidence through the UI. Keep capture exclusion and current account authorization in every evidence path. [Existing evidence and reliability gates](completion-plan.md#product-completion-gap-review--november-16)

### 3. Extend public navigation to user-defined business objectives

Build on the existing public-link workflow. Let the user state a goal for the website under review, then define what the reader must observe to confirm completion. Use a multi-step public information task for the first release. For example, given “Find the published price for this service,” the reader discovers the service, opens its details, and confirms that it can read the associated price. Opening the details page is an intermediate step. Use this example as a regression case and choose the actual task from the user’s objective and the site’s supported actions.

Prove the workflow first on owned fixtures and then on a permitted public website. Cover a completed task, a reproducible barrier, a route that succeeds after an alternate strategy, and a stop or an unconfirmed outcome. Keep independent attempts attributable to their methods and link each conclusion to the actual steps and saved evidence. Report where the reader stopped and what it observed when it cannot confirm completion.

Expand to search, forms, and transactions as supported user objectives require them. Each profile needs a declared goal, observable completion condition, known-good and blocked controls, verified evidence, and authority for its actions. Test forms and transactions in controlled environments. Prove credential capture exclusions and safe retry and cancellation before enabling those profiles. A real submission requires explicit authorization and a confirmed outcome under the approved conditions.

### 4. Validate relevant behavior against NVDA

Maintain a reproducible suite against the current proprietary reader profile. Start updating fixtures alongside stage 1. Compare equivalent initial states, page versions, browser and operating system versions, reader settings, and commands. Cover names, descriptions, states, ordering, browse and focus modes, live regions, forms, errors, and dialogs. Record the actual NVDA output independently, link it to matching steps, and retain agreements, discrepancies, and unresolved coverage. Use known working and blocked cases to detect false positives and missed barriers. [Earlier reference procedure](validation/nvda-reference.md)

For a material finding considered for legal analysis, require matched NVDA validation where technically feasible. Record any reason a comparison could not be performed and make that limitation visible in the determination. Claim NVDA reproduction only when the independent record supports it. Require repeat coverage of affected cases when a reader or relevant environment change can alter behavior. NVDA is part of validation. The proprietary reader remains the investigation engine.

### 5. Deliver evidence-linked legal element assessment

Implement an explicitly scoped assessment for one jurisdiction and entity type first. Resolve jurisdiction, facts about the entity and activity, and the applicable legal standard before assessing elements. Use versioned primary authorities and precedents with pinpoint citations, jurisdiction, effective dates, and recorded research dates. Keep technical criterion findings, the practical effect on the task, and legal relevance individually traceable. Record each element as supported, unsupported, disputed, or undeterminable from the available evidence, with citations and contrary evidence when present. Keep person-specific facts and external evidence distinct from the test record.

Accept using controlled expected assessments that include incomplete jurisdiction facts, disputed applicability, stale or conflicting authorities, evidence gaps, and NVDA disagreements. Each legal statement must link its facts and authoritative sources. Present unavailable or unassessable legal analysis explicitly. Ask qualified reviewers to evaluate the initial rules and representative outputs before enabling that assessment for pilots.

### 6. Qualify supporting tools and release the end-to-end flow

Inventory Mancebo.ai capabilities while stages 1–5 proceed. Record what each available tool can do, what data it can access, how it authenticates, where results are retained, and whether its output is reproducible. Evaluate it for diagnostics, capture and media processing, orchestration, reference validation support, research, and reporting. Connect only the tools that pass these checks. Keep proprietary reader observations as the planner’s website input. Prove that unavailable supporting tools, untrusted tool output, or privileged tool access cannot change the interpreted reader behavior or establish a finding.

Keep the independent core API/private MCP, Supabase Edge gateway, and UI delivery separate. Verify Windows and Linux CI, exact deployment, authorization and hostile-target isolation, documented quotas and retention, rollback/restore, and read-only recovery of saved requests. Run a consented pilot through request, execution, verified export, and determination for the declared profile. Include keyboard and screen-reader use of the UI, alongside a review of how users interpret stopped, inconclusive, completed, and independently reproduced outcomes. Complete user/operator/API documentation against the accepted behavior, then refactor measured complexity.

## First implementation slice

Update the hosted public service-link workflow so the planner selects and activates targets through reader observations. Move DOM link discovery behind diagnostics. Add one alternate heading or landmark route, and exercise both routes against the same controlled browser fixtures. In parallel, continue cancellation attribution and partial-original retention so a stopped investigation leaves reviewable records. Keep the product copy tied to the current evidence assessment until the broader journey, independent validation, and legal-analysis gates pass.
