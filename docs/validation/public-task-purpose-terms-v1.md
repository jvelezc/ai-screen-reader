# Complete task-purpose terms

HTTP1.26.1 corrects future public service-link discovery and goal confirmation. English task terms must be complete words, with explicit supported plurals. `Priceless`, `Discourse`, `Contactless` and `Unhelpful` cannot borrow a task word from within their name or path. `Plant` and `Questionable` cannot confirm Pricing or Help. Combining marks remain part of a word. Slash billing forms adjacent to amounts remain accepted; `/yearbook`, `promonthly` and `one-timer` cannot corroborate an amount as a supported billing period.

This is bounded task-purpose inference, separate from reader announcements. Original names, Domain report validation, historical source/recording bytes, input authority, target policy and native action protocol stay unchanged. No historical assessment is recomputed using the new matcher. The supported profile still tests one declared public service link and makes no whole-site or legal conclusion. It does not add public search or form submission.

| Requirement | Evidence |
| --- | --- |
| Actual visible eligible anchors cannot borrow purpose substrings from names or paths | `GoalDiscoveryRejectsMisleadingTaskSubstrings`:eight actual Chromium cases each reject a decoy-only page, then choose the correct requested link after it; exact kind/name/destination and unchanged document URL are asserted. |
| Supported headings accept complete terms and reject misleading words/combining suffixes | `GoalConfirmationRejectsMisleadingHeadings`:twelve exact snapshot cases across all four kinds, including uppercase/plural success. |
| Billing period cannot borrow a substring | `PricingGoalDoesNotBorrowBillingSubstrings`:three rejected false corroborations. |
| Existing amounts/periods and an adjacent slash remain valid | `PricingGoalUsesObservedAmountAndBillingTerms`:six cases, including `$14.99/month` without a separating space. |
| Existing requested/default selection and no fallback remain intact | `ActualChromiumDiscoveryChoosesTheRequestedServiceInsteadOfAnEarlierPricingLink`; `MissingSelectedServiceDoesNotSilentlyFallBackToPricing`. |

Revision-bound evidence: the initial29-case run has15 failures/14 passes. Four name decoys used paths that the existing target policy already excluded, so they did not reach the intended matching predicate. Corrected eligible cross-service paths produce19 failures/10 passes before the source correction. The original failed receipts remain retained. After correction, the complete browser class passes38/38; related contracts, historical originals, reader/HTTP/private MCP checks pass249/249. Canonical pinned Release/warnings-as-errors passes4,543/4,543 with zero failures/skips. Repository-wide format verification passes after correcting one test initializer's whitespace. Windows/Linux CI and exact promotion are separate pending gates at this source checkpoint.
