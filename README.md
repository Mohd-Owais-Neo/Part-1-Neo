# ProjectNeo
Shariah based Halal stock analyse application

# ProjectNEO - Part 1

ProjectNEO V1 is an automated stock analysis system built with .NET, Azure SQL, Azure Functions, and Razor Pages.

## V1 Features

- Daily scheduled pipeline using Azure Functions
- Market/sector signal processing
- Stock filtering and ranking
- Forbidden sectors exclusion
- Risk management and stop-loss calculation
- Daily email alert
- Azure-hosted web application

## Current Scope

Islamicly / halal permissibility checks are planned for V2 and are not included in V1.

## Scheduler

Production daily execution is handled only by Azure Functions through `DailyRunFunction`.

## Forbidden Sectors

The following sectors are excluded:

- Banking
- Finance
- Insurance
- Entertainment
- Alcohol
- Gambling
- Pornography
- Firearms
