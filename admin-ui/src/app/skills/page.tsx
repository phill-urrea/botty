'use client';

import { useEffect, useState, useCallback } from 'react';
import { Header } from '@/components/layout/header';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { skillsApi, Skill, SkillConfigSchema, SkillTool } from '@/lib/api';
import { cn } from '@/lib/utils';
import { Check, X, Settings, Play, ChevronRight, Eye, EyeOff, AlertCircle, Plug } from 'lucide-react';

export default function SkillsPage() {
  const [skills, setSkills] = useState<Skill[]>([]);
  const [selectedSkill, setSelectedSkill] = useState<Skill | null>(null);
  const [schema, setSchema] = useState<SkillConfigSchema | null>(null);
  const [config, setConfig] = useState<Record<string, string | null>>({});
  const [editedConfig, setEditedConfig] = useState<Record<string, string>>({});
  const [tools, setTools] = useState<SkillTool[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [showSensitive, setShowSensitive] = useState<Record<string, boolean>>({});

  const loadSkills = useCallback(async () => {
    try {
      setLoading(true);
      const response = await skillsApi.list();
      setSkills(response.skills || []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load skills');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadSkills();
  }, [loadSkills]);

  const selectSkill = async (skill: Skill) => {
    setSelectedSkill(skill);
    setError(null);
    try {
      const [schemaRes, configRes, detailRes] = await Promise.all([
        skillsApi.getSchema(skill.id),
        skillsApi.getConfig(skill.id),
        skillsApi.get(skill.id),
      ]);
      const hiddenManagedFields =
        skill.id === 'gmail' || skill.id === 'google-calendar'
          ? new Set(['client_id', 'client_secret', 'accounts'])
          : new Set<string>();
      const filteredSchema: SkillConfigSchema = {
        ...schemaRes,
        fields: schemaRes.fields.filter((field) => !hiddenManagedFields.has(field.key)),
      };
      setSchema(filteredSchema);
      setConfig(configRes.values);
      setTools(detailRes.tools || []);
      
      // Initialize edited config with non-sensitive values
      const edited: Record<string, string> = {};
      filteredSchema.fields.forEach((field) => {
        if (!field.isSensitive && configRes.values[field.key]) {
          edited[field.key] = configRes.values[field.key] || '';
        }
      });
      setEditedConfig(edited);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load skill details');
    }
  };

  const handleSave = async () => {
    if (!selectedSkill) return;
    
    try {
      setSaving(true);
      setError(null);
      await skillsApi.updateConfig(selectedSkill.id, editedConfig);
      // Reload to get updated status
      await loadSkills();
      await selectSkill(selectedSkill);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save configuration');
    } finally {
      setSaving(false);
    }
  };

  const handleValidate = async () => {
    if (!selectedSkill) return;
    
    try {
      const result = await skillsApi.validateConfig(selectedSkill.id);
      if (result.isValid) {
        alert('Configuration is valid!');
      } else {
        alert(`Configuration errors:\n${result.errors.map(e => `- ${e.field}: ${e.message}`).join('\n')}`);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to validate');
    }
  };

  if (loading) {
    return (
      <div className="flex h-full items-center justify-center">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600" />
      </div>
    );
  }

  return (
    <div className="flex flex-col h-full">
      <Header title="Skills" description="Configure and manage assistant skills" />

      <div className="flex-1 flex overflow-hidden">
        {/* Skills List */}
        <div className="w-80 border-r border-gray-200 overflow-auto bg-white">
          <div className="p-4 border-b border-gray-200">
            <h3 className="font-semibold text-gray-900">Available Skills</h3>
          </div>
          <div className="p-2">
            {skills.map((skill) => (
              <button
                key={skill.id}
                onClick={() => selectSkill(skill)}
                className={cn(
                  'w-full p-3 rounded-lg text-left transition-colors',
                  selectedSkill?.id === skill.id
                    ? 'bg-blue-50 border border-blue-200'
                    : 'hover:bg-gray-50'
                )}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Plug className="h-4 w-4 text-gray-500" />
                    <span className="font-medium text-gray-900">{skill.name}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    {skill.isConfigured ? (
                      <Check className="h-4 w-4 text-green-500" />
                    ) : (
                      <X className="h-4 w-4 text-gray-400" />
                    )}
                    <ChevronRight className="h-4 w-4 text-gray-500" />
                  </div>
                </div>
                <p className="text-sm text-gray-600 mt-1 line-clamp-2">
                  {skill.description}
                </p>
                <div className="flex items-center gap-2 mt-2">
                  <Badge variant="outline" className="text-xs">
                    {skill.toolCount} tools
                  </Badge>
                  {skill.isConfigured ? (
                    <Badge variant="success" className="text-xs">Configured</Badge>
                  ) : (
                    <Badge variant="warning" className="text-xs">Setup Required</Badge>
                  )}
                </div>
              </button>
            ))}
          </div>
        </div>

        {/* Skill Configuration */}
        <div className="flex-1 overflow-auto p-6">
          {selectedSkill ? (
            <div className="max-w-2xl space-y-6">
              <div>
                <h2 className="text-xl font-semibold text-gray-900">{selectedSkill.name}</h2>
                <p className="text-gray-700">{selectedSkill.description}</p>
              </div>

              {error && (
                <div className="p-3 rounded-lg bg-red-50 border border-red-200 flex items-center gap-2 text-red-800">
                  <AlertCircle className="h-4 w-4" />
                  {error}
                </div>
              )}

              {/* Configuration Form */}
              {schema && (
                <Card>
                  <CardHeader>
                    <CardTitle className="flex items-center gap-2">
                      <Settings className="h-5 w-5" />
                      Configuration
                    </CardTitle>
                    <CardDescription>
                      Configure the skill settings. Sensitive values are stored securely.
                      {(selectedSkill.id === 'gmail' || selectedSkill.id === 'google-calendar') &&
                        ' OAuth account linking is managed in Settings.'}
                    </CardDescription>
                  </CardHeader>
                  <CardContent className="space-y-4">
                    {schema.fields.map((field) => (
                      <div key={field.key}>
                        <label className="block text-sm font-medium text-gray-700 mb-1">
                          {field.label}
                          {field.isRequired && <span className="text-red-500 ml-1">*</span>}
                          {field.isSensitive && (
                            <Badge variant="secondary" className="ml-2 text-xs">Sensitive</Badge>
                          )}
                        </label>
                        {field.description && (
                          <p className="text-xs text-gray-600 mb-2">{field.description}</p>
                        )}
                        <div className="relative">
                          {field.type === 'json' ? (
                            <Textarea
                              value={editedConfig[field.key] || ''}
                              onChange={(e) => setEditedConfig(prev => ({
                                ...prev,
                                [field.key]: e.target.value
                              }))}
                              placeholder={field.isSensitive && config[field.key] ? '********' : field.defaultValue || ''}
                              rows={4}
                            />
                          ) : (
                            <Input
                              type={field.isSensitive && !showSensitive[field.key] ? 'password' : 'text'}
                              value={editedConfig[field.key] || ''}
                              onChange={(e) => setEditedConfig(prev => ({
                                ...prev,
                                [field.key]: e.target.value
                              }))}
                              placeholder={field.isSensitive && config[field.key] ? '********' : field.defaultValue || ''}
                            />
                          )}
                          {field.isSensitive && (
                            <button
                              type="button"
                              className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600"
                              onClick={() => setShowSensitive(prev => ({
                                ...prev,
                                [field.key]: !prev[field.key]
                              }))}
                            >
                              {showSensitive[field.key] ? (
                                <EyeOff className="h-4 w-4" />
                              ) : (
                                <Eye className="h-4 w-4" />
                              )}
                            </button>
                          )}
                        </div>
                      </div>
                    ))}

                    <div className="flex items-center gap-2 pt-4">
                      <Button onClick={handleSave} disabled={saving}>
                        {saving ? 'Saving...' : 'Save Configuration'}
                      </Button>
                      <Button variant="outline" onClick={handleValidate}>
                        Validate
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              )}

              {/* Tools */}
              {tools.length > 0 && (
                <Card>
                  <CardHeader>
                    <CardTitle className="flex items-center gap-2">
                      <Play className="h-5 w-5" />
                      Available Tools
                    </CardTitle>
                    <CardDescription>
                      Tools this skill provides to the assistant
                    </CardDescription>
                  </CardHeader>
                  <CardContent>
                    <div className="space-y-3">
                      {tools.map((tool) => (
                        <div key={tool.name} className="p-3 border border-gray-200 rounded-lg">
                          <code className="text-sm font-mono text-blue-600">{tool.name}</code>
                          <p className="text-sm text-gray-600 mt-1">{tool.description}</p>
                        </div>
                      ))}
                    </div>
                  </CardContent>
                </Card>
              )}
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center h-full text-gray-600">
              <Plug className="h-12 w-12 mb-4" />
              <p>Select a skill to configure</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
