# Changelog

## 0.1.0 (2026-10-12)


### Features

* **api:** add calendar grants ([3da3915](https://github.com/VanDooProject/sCalenderPlus/commit/3da39154009f17dfbdd49f520ecfc52bb3615de5)), closes [#42](https://github.com/VanDooProject/sCalenderPlus/issues/42)
* **api:** add calendars CRUD for user- and group-owned calendars ([2ddfcca](https://github.com/VanDooProject/sCalenderPlus/commit/2ddfcca3257eddd704d72ef66cd76dcffea1ea7f)), closes [#41](https://github.com/VanDooProject/sCalenderPlus/issues/41)
* **api:** add events CRUD for single events ([fe0b60c](https://github.com/VanDooProject/sCalenderPlus/commit/fe0b60ceecc47c38cc8d1bc54221df423eff6070)), closes [#44](https://github.com/VanDooProject/sCalenderPlus/issues/44)
* **api:** add forwarded headers, data protection key storage and api v1 group ([879de1e](https://github.com/VanDooProject/sCalenderPlus/commit/879de1e59d94c2386d2fb655e6d5e3eb05452552)), closes [#25](https://github.com/VanDooProject/sCalenderPlus/issues/25)
* **api:** add group invites by email and by link ([e159cee](https://github.com/VanDooProject/sCalenderPlus/commit/e159ceeb0c4ade000e6d9cb48cc94d23b005d5a8)), closes [#36](https://github.com/VanDooProject/sCalenderPlus/issues/36)
* **api:** add group membership management ([a4a1bc8](https://github.com/VanDooProject/sCalenderPlus/commit/a4a1bc84282268f678edc34829df9b8393da283f)), closes [#35](https://github.com/VanDooProject/sCalenderPlus/issues/35)
* **api:** add groups CRUD with roles ([8138dd8](https://github.com/VanDooProject/sCalenderPlus/commit/8138dd8720f075807a6142ddcf9bf45d0a60b650)), closes [#34](https://github.com/VanDooProject/sCalenderPlus/issues/34)
* **api:** add health endpoints, healthcheck command and structured logging ([6667590](https://github.com/VanDooProject/sCalenderPlus/commit/66675909d7437454082cf610cb25739c5fa02d8d)), closes [#18](https://github.com/VanDooProject/sCalenderPlus/issues/18)
* **api:** add problem details and stable error code catalogue ([60e4839](https://github.com/VanDooProject/sCalenderPlus/commit/60e4839f5b75e1fdb06cbe9947b1bc428e787313)), closes [#25](https://github.com/VanDooProject/sCalenderPlus/issues/25)
* **api:** add rate limiting baseline and disposable-email blocklist ([41362dd](https://github.com/VanDooProject/sCalenderPlus/commit/41362dd8f901a9c43c2550738081c2e44f68d98d)), closes [#30](https://github.com/VanDooProject/sCalenderPlus/issues/30)
* **api:** add recurring events and occurrence edits ([02cda65](https://github.com/VanDooProject/sCalenderPlus/commit/02cda6506459f5c66d4198c14d05fac1578d793c)), closes [#50](https://github.com/VanDooProject/sCalenderPlus/issues/50) [#51](https://github.com/VanDooProject/sCalenderPlus/issues/51)
* **api:** add the event window query with permission filtering ([f5fdf3e](https://github.com/VanDooProject/sCalenderPlus/commit/f5fdf3e6e7370397f43d4d5c29c913a24f7b93c7)), closes [#45](https://github.com/VanDooProject/sCalenderPlus/issues/45)
* **api:** add the invite preview endpoint ([2f3b9c3](https://github.com/VanDooProject/sCalenderPlus/commit/2f3b9c3fa0ab4b858f6907c704130176940904e9)), closes [#55](https://github.com/VanDooProject/sCalenderPlus/issues/55)
* **api:** add the personal calendar overlay (hidden, color) ([b3e98ba](https://github.com/VanDooProject/sCalenderPlus/commit/b3e98baee3fd9c8e705487a0345a6c24ae314329)), closes [#56](https://github.com/VanDooProject/sCalenderPlus/issues/56)
* **api:** add user profile settings ([d97614c](https://github.com/VanDooProject/sCalenderPlus/commit/d97614cc9c2404b58830ed577c53d970ea81a718)), closes [#33](https://github.com/VanDooProject/sCalenderPlus/issues/33)
* **api:** enforce group lifecycle rules for calendars ([205ba51](https://github.com/VanDooProject/sCalenderPlus/commit/205ba51970d9a0407df4569798c4b7be9147cec7)), closes [#43](https://github.com/VanDooProject/sCalenderPlus/issues/43)
* **api:** move events between calendars ([692dc89](https://github.com/VanDooProject/sCalenderPlus/commit/692dc89736824d29e9ac8450371d1166d67795ba)), closes [#48](https://github.com/VanDooProject/sCalenderPlus/issues/48)
* **api:** revoke event shares when calendar access is lost ([f9e477b](https://github.com/VanDooProject/sCalenderPlus/commit/f9e477bd49534f7342d242d1b0e8436a3e718f42)), closes [#49](https://github.com/VanDooProject/sCalenderPlus/issues/49)
* **auth:** add ASP.NET Core Identity with cookie sessions ([d268096](https://github.com/VanDooProject/sCalenderPlus/commit/d268096b344c52bb6b5d2aae34fc3ac080d01ee8)), closes [#28](https://github.com/VanDooProject/sCalenderPlus/issues/28)
* **auth:** add TOTP two-factor authentication with recovery codes ([4d660c0](https://github.com/VanDooProject/sCalenderPlus/commit/4d660c0a9afd0c70833d6af5bbdb46c1acc5ed81)), closes [#31](https://github.com/VanDooProject/sCalenderPlus/issues/31)
* **auth:** require the CSRF header on unsafe api requests ([9466d41](https://github.com/VanDooProject/sCalenderPlus/commit/9466d41079a195c7598e0b3e745741892120366b)), closes [#29](https://github.com/VanDooProject/sCalenderPlus/issues/29)
* **core:** add audit event recording ([c7f8bea](https://github.com/VanDooProject/sCalenderPlus/commit/c7f8beab9cad09eec9789ec0811423e165d4695a)), closes [#32](https://github.com/VanDooProject/sCalenderPlus/issues/32)
* **core:** add the entitlement service with plan limits from configuration ([d8636b6](https://github.com/VanDooProject/sCalenderPlus/commit/d8636b6c5c5df44ed5d7c0529e32210fb68c7883)), closes [#52](https://github.com/VanDooProject/sCalenderPlus/issues/52)
* **core:** add the recurrence expansion engine ([8dbdd26](https://github.com/VanDooProject/sCalenderPlus/commit/8dbdd26e37883d796ce8ddec24e6105663b745d0)), closes [#50](https://github.com/VanDooProject/sCalenderPlus/issues/50)
* **db:** add EF Core with Npgsql and NodaTime, first migration and migrate command ([5ea7d47](https://github.com/VanDooProject/sCalenderPlus/commit/5ea7d4730bd0cc1c54db57def426222cb017e67c)), closes [#17](https://github.com/VanDooProject/sCalenderPlus/issues/17)
* **perm:** add the access explain endpoint ([2b50d0c](https://github.com/VanDooProject/sCalenderPlus/commit/2b50d0c60d894f42c0e30b2af4fb7c1f1dfd9596)), closes [#47](https://github.com/VanDooProject/sCalenderPlus/issues/47)
* **perm:** add the event permission overrides api ([da8ae11](https://github.com/VanDooProject/sCalenderPlus/commit/da8ae11edfc60f8336b5d78f6108966727759434)), closes [#46](https://github.com/VanDooProject/sCalenderPlus/issues/46)
* **perm:** add the pure permission engine ([dbcb694](https://github.com/VanDooProject/sCalenderPlus/commit/dbcb69492ee1fcaf735fe10462b93692f5379f61)), closes [#40](https://github.com/VanDooProject/sCalenderPlus/issues/40)
* **web:** add calendar settings, sharing and the create calendar dialog ([77d8124](https://github.com/VanDooProject/sCalenderPlus/commit/77d81240d4dece8063ec43345daf8ed4c8df29a0)), closes [#57](https://github.com/VanDooProject/sCalenderPlus/issues/57)
* **web:** add groups, members and invites pages ([2f1473c](https://github.com/VanDooProject/sCalenderPlus/commit/2f1473c2a812cb56beb9043712a126a035233762)), closes [#55](https://github.com/VanDooProject/sCalenderPlus/issues/55)
* **web:** add month, week, day and agenda calendar views ([308ed17](https://github.com/VanDooProject/sCalenderPlus/commit/308ed1783252664542c59a120c63ce5a1c5ed585)), closes [#56](https://github.com/VanDooProject/sCalenderPlus/issues/56)
* **web:** add the app shell, auth pages and account settings ([7862a71](https://github.com/VanDooProject/sCalenderPlus/commit/7862a7140809aa561d4b918e7b6cc89e8d237807))
* **web:** event create/edit dialog ([f7d454c](https://github.com/VanDooProject/sCalenderPlus/commit/f7d454c54cc2f38ee20c16a0d7674cfb41e91477)), closes [#58](https://github.com/VanDooProject/sCalenderPlus/issues/58)
* **web:** event permission editor and "why can X see this?" panel ([6919c6a](https://github.com/VanDooProject/sCalenderPlus/commit/6919c6a84a4b81b3e7d6cba93e3773ee3e2db0e3)), closes [#61](https://github.com/VanDooProject/sCalenderPlus/issues/61) [#60](https://github.com/VanDooProject/sCalenderPlus/issues/60)
* **web:** move an event to another calendar ([3f379d0](https://github.com/VanDooProject/sCalenderPlus/commit/3f379d0899d27263b23a6b52b07f088f16511e2e)), closes [#61](https://github.com/VanDooProject/sCalenderPlus/issues/61)
* **worker:** add Postgres job queue and email sending ([4404f82](https://github.com/VanDooProject/sCalenderPlus/commit/4404f82ceea7eeb30d12a64744edac0412001f1d)), closes [#27](https://github.com/VanDooProject/sCalenderPlus/issues/27)


### Bug Fixes

* **api:** bump acl versions on calendar lifecycle and role default changes ([6e36f6d](https://github.com/VanDooProject/sCalenderPlus/commit/6e36f6da4e66ed7ede7623d7ae036d9376290af5)), closes [#41](https://github.com/VanDooProject/sCalenderPlus/issues/41) [#49](https://github.com/VanDooProject/sCalenderPlus/issues/49)
* **api:** declare integers as integers in the OpenAPI document ([096e320](https://github.com/VanDooProject/sCalenderPlus/commit/096e320e0e14c2918f2c7d249e88b7a7e41dfd09)), closes [#25](https://github.com/VanDooProject/sCalenderPlus/issues/25)
* **api:** let owners clean up frozen calendars ([6134a0e](https://github.com/VanDooProject/sCalenderPlus/commit/6134a0e74e43675670a402c124b427b5c272b86d)), closes [#42](https://github.com/VanDooProject/sCalenderPlus/issues/42) [#44](https://github.com/VanDooProject/sCalenderPlus/issues/44) [#48](https://github.com/VanDooProject/sCalenderPlus/issues/48)
* **api:** list only busy-relevant exceptions in the busy projection ([9f3b510](https://github.com/VanDooProject/sCalenderPlus/commit/9f3b5105c307c7c285603193be21539a4d4a343c)), closes [#50](https://github.com/VanDooProject/sCalenderPlus/issues/50)
* **api:** make the healthcheck command follow ASPNETCORE_URLS and bypass proxies ([93c5ed3](https://github.com/VanDooProject/sCalenderPlus/commit/93c5ed35aa81e5e2075f3b5b3e13cbcb8b6b5de5)), closes [#18](https://github.com/VanDooProject/sCalenderPlus/issues/18)
* **api:** register job handlers only in the worker ([1fb7cc8](https://github.com/VanDooProject/sCalenderPlus/commit/1fb7cc8c553bfd663a1821aeee4be106b204ebee)), closes [#55](https://github.com/VanDooProject/sCalenderPlus/issues/55)
* **api:** split series only at occurrences of the rule at their own time ([a26b101](https://github.com/VanDooProject/sCalenderPlus/commit/a26b10126955cb1f441851f684ddeab69806efea)), closes [#51](https://github.com/VanDooProject/sCalenderPlus/issues/51)
* **auth:** hash the password on logins of locked-out accounts ([3ec010b](https://github.com/VanDooProject/sCalenderPlus/commit/3ec010be74ba902e3e2c5650fdc013d5f803ffbd)), closes [#28](https://github.com/VanDooProject/sCalenderPlus/issues/28)
* **auth:** require the password to enable 2FA and lock out failed confirmations ([a7d4225](https://github.com/VanDooProject/sCalenderPlus/commit/a7d42258e4a1797d88d561d280b50ee46dd31bfc)), closes [#31](https://github.com/VanDooProject/sCalenderPlus/issues/31)
* **ci:** write the engine coverage report to a fixed directory ([f61bba5](https://github.com/VanDooProject/sCalenderPlus/commit/f61bba5c210b949e0d5bc8f6a1dddf716568d53b)), closes [#40](https://github.com/VanDooProject/sCalenderPlus/issues/40)
* **core:** bound the recurrence expansion cost of a window query ([7374808](https://github.com/VanDooProject/sCalenderPlus/commit/7374808a3f1123876e24f5f2835a483c1dcf8327)), closes [#50](https://github.com/VanDooProject/sCalenderPlus/issues/50) [#45](https://github.com/VanDooProject/sCalenderPlus/issues/45)
* **core:** find zero-length series at their last occurrence ([4201170](https://github.com/VanDooProject/sCalenderPlus/commit/4201170d9f9ebbf7a2974b632782b91985ba27eb)), closes [#50](https://github.com/VanDooProject/sCalenderPlus/issues/50)
* **db:** disable GSS encryption by default to keep container logs JSON-only ([7469095](https://github.com/VanDooProject/sCalenderPlus/commit/746909593e751f324f94ef3447ae4b4881b94ddb)), closes [#17](https://github.com/VanDooProject/sCalenderPlus/issues/17)
* **deploy:** bound Coolify API calls and keep the token off the command line ([2662bf0](https://github.com/VanDooProject/sCalenderPlus/commit/2662bf04b486d070aac8940f0dc8e77742d235e3)), closes [#24](https://github.com/VanDooProject/sCalenderPlus/issues/24)
* **deploy:** redact email-link tokens in Caddy logs ([e4714eb](https://github.com/VanDooProject/sCalenderPlus/commit/e4714eba64da6f50b9e32a02579c3d3c627a62e6)), closes [#28](https://github.com/VanDooProject/sCalenderPlus/issues/28) [#36](https://github.com/VanDooProject/sCalenderPlus/issues/36)
* **deploy:** redact iCal tokens in Caddy error logs and non-canonical paths ([58aaae9](https://github.com/VanDooProject/sCalenderPlus/commit/58aaae9af457382af5ce79a69f1a10e6ae219947)), closes [#20](https://github.com/VanDooProject/sCalenderPlus/issues/20)
* **docker:** keep restored NuGet packages in the restore layer ([ebd45b7](https://github.com/VanDooProject/sCalenderPlus/commit/ebd45b712fa252c9526ea353b75ca0b50bbbf057))
* **groups:** always write the group row on membership changes ([bfc96d1](https://github.com/VanDooProject/sCalenderPlus/commit/bfc96d125601f4d3dea0f786b3fdd5e25ce3a2df)), closes [#35](https://github.com/VanDooProject/sCalenderPlus/issues/35)
* **groups:** refuse joins of frozen groups and let concurrent joins pass ([2f7f1db](https://github.com/VanDooProject/sCalenderPlus/commit/2f7f1dbd8350d4614d84a3ee762faeedd1efdfb4)), closes [#36](https://github.com/VanDooProject/sCalenderPlus/issues/36)
* **perm:** close override-change gaps on removals and moves ([3b5e229](https://github.com/VanDooProject/sCalenderPlus/commit/3b5e229938607d67923040dd4c6e2bb1d96211ad)), closes [#40](https://github.com/VanDooProject/sCalenderPlus/issues/40)
* **web:** allow reka-ui's fixed viewport styles in the CSP by hash ([86f9736](https://github.com/VanDooProject/sCalenderPlus/commit/86f9736c1c95c1ebfc3bf53cedf321a6db5fd0cf)), closes [#57](https://github.com/VanDooProject/sCalenderPlus/issues/57)
* **web:** list an edited event's own calendar in the dialog ([7527e0e](https://github.com/VanDooProject/sCalenderPlus/commit/7527e0ef0d4bbfe1d7f9c10f5957901bfb1571b6)), closes [#58](https://github.com/VanDooProject/sCalenderPlus/issues/58)
* **web:** set the document language at startup and proxy /openapi in dev ([9f84676](https://github.com/VanDooProject/sCalenderPlus/commit/9f84676a3066c45a9a217f50d103f648505ba8d2)), closes [#14](https://github.com/VanDooProject/sCalenderPlus/issues/14)


### Performance Improvements

* **api:** query "Shared with me" and lifecycle revocations by what they need ([b1e6420](https://github.com/VanDooProject/sCalenderPlus/commit/b1e6420cdd672581e6bc66981d4a6ca170aef4f4)), closes [#45](https://github.com/VanDooProject/sCalenderPlus/issues/45) [#49](https://github.com/VanDooProject/sCalenderPlus/issues/49)
* **perm:** add an allocation-free untraced resolution for listings ([2447639](https://github.com/VanDooProject/sCalenderPlus/commit/244763910b7a718ee67c7752f9980380877f1b16)), closes [#40](https://github.com/VanDooProject/sCalenderPlus/issues/40)


### Documentation

* add M0 code review findings ([2674546](https://github.com/VanDooProject/sCalenderPlus/commit/2674546aef9cefb95dfcd531c95a7e02722a81f7)), closes [#1](https://github.com/VanDooProject/sCalenderPlus/issues/1)
* add product and architecture specification ([3eb8136](https://github.com/VanDooProject/sCalenderPlus/commit/3eb81363eb946daa3d1181272bba37319d331450))
* add screenshot gallery ([7f3c034](https://github.com/VanDooProject/sCalenderPlus/commit/7f3c034beeb4eab62316ffa23d992f791a857187))
* address spec review findings ([50d5c1d](https://github.com/VanDooProject/sCalenderPlus/commit/50d5c1ded5a01a8ec756f40ac69ea41a76e232c6))
* align README and deployment docs with the healthcheck and dev proxy ([20b676e](https://github.com/VanDooProject/sCalenderPlus/commit/20b676e5bb7d8f8a35e6853cce3af8367d9eaf27)), closes [#18](https://github.com/VanDooProject/sCalenderPlus/issues/18)
* **docs:** describe the web app structure, error mapping and test setup ([c23c7a8](https://github.com/VanDooProject/sCalenderPlus/commit/c23c7a8362651689bbeb5e29c255259612092944))
* **docs:** document recurrence storage, expansion and occurrence edits ([fb80a0d](https://github.com/VanDooProject/sCalenderPlus/commit/fb80a0df962b3763d8424ec0b53308df410e5235)), closes [#50](https://github.com/VanDooProject/sCalenderPlus/issues/50) [#51](https://github.com/VanDooProject/sCalenderPlus/issues/51)
* overview.md now names the .slnx solution and the test platform. ([d4542b8](https://github.com/VanDooProject/sCalenderPlus/commit/d4542b8204989c71376644cfc4c3297aa2bff6f7))
* **reviews:** add the M1 security review ([3b83f33](https://github.com/VanDooProject/sCalenderPlus/commit/3b83f331f208f07b5fb805996eb38e1694da1010)), closes [#25](https://github.com/VanDooProject/sCalenderPlus/issues/25) [#36](https://github.com/VanDooProject/sCalenderPlus/issues/36)
* **reviews:** add the M2 review ([7ff87c2](https://github.com/VanDooProject/sCalenderPlus/commit/7ff87c25df05e3dcdaf07344ae86d56e35f3f2b9)), closes [#52](https://github.com/VanDooProject/sCalenderPlus/issues/52)
* **reviews:** add the permission engine review ([cf5fa87](https://github.com/VanDooProject/sCalenderPlus/commit/cf5fa87ca55cb90fae84a70c0000ca298c049356)), closes [#40](https://github.com/VanDooProject/sCalenderPlus/issues/40)
* screenshots of the event permission editor and the access explainer ([f8737dc](https://github.com/VanDooProject/sCalenderPlus/commit/f8737dc459386cb087d80ed882a0ea9cf6ffa65e)), closes [#61](https://github.com/VanDooProject/sCalenderPlus/issues/61)
