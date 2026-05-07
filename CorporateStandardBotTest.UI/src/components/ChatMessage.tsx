import { useMemo, type ReactNode } from "react"

import type { components } from "@/api/schema"
import { HoverCard, HoverCardContent, HoverCardTrigger } from "./ui/hover-card";
import { ArrowUpRightIcon } from "lucide-react";
import { Badge } from "./ui/badge";

type AiChatMessage = components["schemas"]["AiChatMessage"]

function contentToHtml(message: AiChatMessage) {
  const elements = [] as ReactNode[];
  const regex = /\[ref_id:(\d+)\]/

  let reminder = message.content
  let match = reminder.match(regex)
  while (match) {
    const lines = reminder.substring(0, match.index).split('\n')
    for (let i = 0; i < lines.length; i++) {
      if (i > 0)
        elements.push(<br />)

      elements.push(lines[i])
    }

    const [fullMatch, id] = match
    const reference = message.references?.find(r => r.id === id)
    if (!reference) {
      elements.push(`[missing reference]`)
    } else {
      elements.push(
        <HoverCard>
          <HoverCardTrigger render={<a href={reference.url} target="_blank">[{id}]</a>} />
          <HoverCardContent>
            <a href={reference.url} target="_blank">{reference.name}</a>
          </HoverCardContent>
        </HoverCard>
      )
    }
    reminder = reminder.substring((match.index ?? 0) + fullMatch.length)
    match = reminder.match(regex)
  }

  if (reminder.length > 0)
    elements.push(reminder)

  return elements
}

export function ChatMessage({ message }: { message: AiChatMessage }) {
  const { html, references } = useMemo(() => {
    const html = contentToHtml(message)
    const references = [...(message.references ?? [])].sort((a, b) =>
      a.id.localeCompare(b.id, undefined, { numeric: true, sensitivity: "base" })
    )

    return { html, references }
  }, [message])

  return (
    <div className="flex justify-start">
      <article className="max-w-[85%] rounded-2xl border border-border/70 bg-card px-4 py-3 text-sm shadow-sm">
        <div
          className="wrap-break-word leading-6 text-card-foreground [&_a]:font-medium [&_a]:text-foreground [&_a]:underline [&_a]:underline-offset-4"
        >
          {html}
        </div>

        {references.length > 0 ? (
          <div className="mt-4 border-t border-border/70 pt-3 flex flex-col gap-3">
            <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
              References
            </p>
            <div className="flex flex-wrap gap-2">
              {references.map((reference) => (
                <Badge key={reference.id} className="text-sm max-w-full min-w-0 whitespace-normal" variant="outline" render={
                  <a href={reference.url} target="_blank">
                    <span className="min-w-0 truncate">
                      {reference.name}
                    </span>
                    <ArrowUpRightIcon data-icon="inline-end" className="shrink-0" />
                  </a>} />
              ))}
            </div>
          </div>
        ) : null}
      </article>
    </div>
  )
}
