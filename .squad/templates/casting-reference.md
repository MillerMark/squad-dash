# Casting Reference

On-demand reference for Squad's casting system. Loaded during Init Mode or when adding team members.

## Universe Table

| Universe | Capacity | Shape Tags | Resonance Signals |
|---|---|---|---|
| The Usual Suspects | 6 | small, noir, ensemble | crime, heist, mystery, deception |
| Reservoir Dogs | 8 | small, noir, ensemble | crime, heist, tension, loyalty |
| Alien | 8 | small, sci-fi, survival | space, isolation, threat, engineering |
| Ocean's Eleven | 14 | medium, heist, ensemble | planning, coordination, roles, charm |
| Arrested Development | 15 | medium, comedy, ensemble | dysfunction, business, family, satire |
| Star Wars | 12 | medium, sci-fi, epic | conflict, mentorship, legacy, rebellion |
| The Matrix | 10 | medium, sci-fi, cyberpunk | systems, reality, hacking, philosophy |
| Firefly | 10 | medium, sci-fi, western | frontier, crew, independence, smuggling |
| The Goonies | 8 | small, adventure, ensemble | exploration, treasure, kids, teamwork |
| The Simpsons | 20 | large, comedy, ensemble | satire, community, family, absurdity |
| Breaking Bad | 12 | medium, drama, tension | chemistry, transformation, consequence, power |
| Lost | 18 | large, mystery, ensemble | survival, mystery, groups, leadership |
| Marvel Cinematic Universe | 25 | large, action, ensemble | heroism, teamwork, powers, scale |
| DC Universe | 18 | large, action, ensemble | justice, duality, powers, mythology |
| Futurama | 12 | medium, sci-fi, comedy | future, robots, space, absurdity |

**Total: 15 universes** — capacity range 6–25.

## Character Pools

Eligible characters per universe. Casting draws from this pool when assembling a team. Names are listed in priority/prominence order.

### The Usual Suspects (6)
Verbal Kint, Dean Keaton, Michael McManus, Fred Fenster, Todd Hockney, Dave Kujan

### Reservoir Dogs (8)
Mr. White, Mr. Orange, Mr. Blonde, Mr. Pink, Joe Cabot, Nice Guy Eddie, Mr. Blue, Mr. Brown

### Alien (8)
Ellen Ripley, Dallas, Kane, Lambert, Parker, Ash, Brett, Bishop

### Ocean's Eleven (14)
Danny Ocean, Rusty Ryan, Linus Caldwell, Reuben Tishkoff, Frank Catton, Livingston Dell, Basher Tarr, Saul Bloom, Virgil Malloy, Turk Malloy, Yen, Isabel Lahiri, Roman Nagel, Lenny Pepperidge

### Arrested Development (15)
Michael Bluth, George Michael Bluth, George Bluth Sr., Lucille Bluth, G.O.B. Bluth, Lindsay Bluth Fünke, Tobias Fünke, Maeby Fünke, Buster Bluth, Lucille Austero, Barry Zuckerkorn, Ann Veal, Annyong, Steve Holt, Herb Love

### Star Wars (12)
Luke Skywalker, Han Solo, Princess Leia, Obi-Wan Kenobi, Yoda, Darth Vader, R2-D2, C-3PO, Chewbacca, Lando Calrissian, Ahsoka Tano, Mace Windu

### The Matrix (10)
Neo, Trinity, Morpheus, Agent Smith, The Oracle, Tank, Dozer, Apoc, Switch, Mouse

### Firefly (10)
Malcolm Reynolds, Zoe Washburne, Hoban Washburne, Jayne Cobb, Kaylee Frye, Inara Serra, Simon Tam, River Tam, Shepherd Book, Saffron

### The Goonies (8)
Mikey Walsh, Brand Walsh, Chunk Cohen, Mouth Devereaux, Data Wang, Andy, Stef, Sloth Fratelli

### The Simpsons (20)
Homer Simpson, Marge Simpson, Bart Simpson, Lisa Simpson, Ned Flanders, Moe Szyslak, Barney Gumble, Chief Wiggum, Apu Nahasapeemapetilon, Mr. Burns, Waylon Smithers, Krusty the Clown, Sideshow Bob, Milhouse Van Houten, Nelson Muntz, Ralph Wiggum, Lenny Leonard, Carl Carlson, Professor Frink, Comic Book Guy

### Breaking Bad (12)
Walter White, Jesse Pinkman, Hank Schrader, Mike Ehrmantraut, Saul Goodman, Gustavo Fring, Skyler White, Marie Schrader, Walter White Jr., Todd Alquist, Lydia Rodarte-Quayle, Skinny Pete

### Lost (18)
Jack Shephard, Kate Austen, John Locke, James "Sawyer" Ford, Hugo "Hurley" Reyes, Jin-Soo Kwon, Sun-Hwa Kwon, Charlie Pace, Sayid Jarrah, Claire Littleton, Desmond Hume, Benjamin Linus, Michael Dawson, Shannon Rutherford, Boone Carlyle, Ana Lucia Cortez, Mr. Eko, Walt Lloyd

### Marvel Cinematic Universe (25)
Tony Stark, Steve Rogers, Thor, Natasha Romanoff, Bruce Banner, Clint Barton, Nick Fury, Peter Parker, Doctor Strange, T'Challa, Carol Danvers, Scott Lang, Hope Van Dyne, Vision, Wanda Maximoff, Sam Wilson, Bucky Barnes, Loki, Groot, Rocket Raccoon, Peter Quill, Gamora, Drax, Nebula, Shuri

### DC Universe (18)
Bruce Wayne, Clark Kent, Diana Prince, Barry Allen, Hal Jordan, Arthur Curry, Oliver Queen, Dinah Lance, Victor Stone, J'onn J'onzz, Dick Grayson, Tim Drake, Barbara Gordon, Alfred Pennyworth, Commissioner Gordon, Lex Luthor, Joker, Harley Quinn

### Futurama (12)
Philip J. Fry, Turanga Leela, Bender Bending Rodriguez, Professor Hubert J. Farnsworth, Amy Wong, Hermes Conrad, Dr. John Zoidberg, Zapp Brannigan, Kif Kroker, Mom, Calculon, Nibbler

## Selection Algorithm

Universe selection is deterministic. Score each universe and pick the highest:

```
score = size_fit + shape_fit + resonance_fit + LRU
```

| Factor | Description |
|---|---|
| `size_fit` | How well the universe capacity matches the team size. Prefer universes where capacity ≥ agent_count with minimal waste. |
| `shape_fit` | Match universe shape tags against the assignment shape derived from the project description. |
| `resonance_fit` | Match universe resonance signals against session and repo context signals. |
| `LRU` | Least-recently-used bonus — prefer universes not used in recent assignments (from `history.json`). |

Same inputs → same choice (unless LRU changes between assignments).

## Casting State File Schemas

### policy.json

Source template: `.squad/templates/casting-policy.json`
Runtime location: `.squad/casting/policy.json`

```json
{
  "casting_policy_version": "1.1",
  "allowlist_universes": ["Universe Name", "..."],
  "universe_capacity": {
    "Universe Name": 10
  }
}
```

### registry.json

Source template: `.squad/templates/casting-registry.json`
Runtime location: `.squad/casting/registry.json`

```json
{
  "agents": {
    "agent-role-id": {
      "persistent_name": "CharacterName",
      "universe": "Universe Name",
      "created_at": "ISO-8601",
      "legacy_named": false,
      "status": "active"
    }
  }
}
```

### history.json

Source template: `.squad/templates/casting-history.json`
Runtime location: `.squad/casting/history.json`

```json
{
  "universe_usage_history": [
    {
      "universe": "Universe Name",
      "assignment_id": "unique-id",
      "used_at": "ISO-8601"
    }
  ],
  "assignment_cast_snapshots": {
    "assignment-id": {
      "universe": "Universe Name",
      "agents": {
        "role-id": "CharacterName"
      },
      "created_at": "ISO-8601"
    }
  }
}
```
